using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.API.Routing
{
    public sealed class RouterOptions
    {
        /// <summary>Whether the Minecraft router listens at all. Defaults on for the control plane.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>The single public port every routed server is reached through (Java default 25565).</summary>
        public int ListenPort { get; set; } = 25565;

        /// <summary>How long a client has to send its handshake before it's dropped - keeps port
        /// scanners and half-open connections from tying up sockets.</summary>
        public int HandshakeTimeoutSeconds { get; set; } = 5;

        /// <summary>How long to wait when dialing a backend before treating it as down/asleep.</summary>
        public int BackendConnectTimeoutSeconds { get; set; } = 2;

        /// <summary>Stop servers that have had no players for <see cref="AutoSleepIdleMinutes"/>.
        /// Wake-on-connect always works; this is what makes servers sleep in the first place.</summary>
        public bool AutoSleepEnabled { get; set; }

        public int AutoSleepIdleMinutes { get; set; } = 10;
    }

    /// <summary>
    /// A hostname-aware Minecraft (Java) reverse proxy with wake-on-connect. It listens on one port,
    /// reads each client's handshake to learn the address the player typed, and pipes to the matching
    /// backend. If that backend is asleep, a status ping shows a "sleeping" MOTD and a login attempt
    /// starts the container and asks the player to rejoin.
    /// </summary>
    public sealed class MinecraftRouter(
        IServiceScopeFactory scopeFactory,
        IServerConnectionTracker tracker,
        IOptions<RouterOptions> options,
        ILogger<MinecraftRouter> logger) : BackgroundService
    {
        private readonly RouterOptions _options = options.Value;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                logger.LogInformation("Minecraft router disabled (Router:Enabled=false).");
                return;
            }

            var listener = CreateListener(_options.ListenPort);
            listener.Start();
            logger.LogInformation("Minecraft router listening on port {Port}.", _options.ListenPort);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await listener.AcceptTcpClientAsync(stoppingToken); }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { logger.LogDebug(ex, "Accept failed; continuing."); continue; }

                    _ = HandleClientAsync(client, stoppingToken);
                }
            }
            finally { listener.Stop(); }
        }

        private static TcpListener CreateListener(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.IPv6Any, port);
                listener.Server.DualMode = true;
                return listener;
            }
            catch { return new TcpListener(IPAddress.Any, port); }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken stoppingToken)
        {
            using var _ = client;
            client.NoDelay = true;

            TcpClient? backend = null;
            try
            {
                var clientStream = client.GetStream();

                using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                handshakeCts.CancelAfter(TimeSpan.FromSeconds(_options.HandshakeTimeoutSeconds));

                var lengthResult = await MinecraftProtocol.ReadVarIntAsync(clientStream, handshakeCts.Token);
                if (lengthResult is null) return;

                var length = lengthResult.Value;
                if (length <= 0 || length > 65536) { logger.LogDebug("Dropping implausible packet length {Length}.", length); return; }

                var body = new byte[length];
                await clientStream.ReadExactlyAsync(body.AsMemory(0, length), handshakeCts.Token);

                var handshake = MinecraftProtocol.ParseHandshake(body);
                if (handshake is null) { logger.LogDebug("Dropping unparseable handshake."); return; }

                var hostname = MinecraftProtocol.SanitizeAddress(handshake.ServerAddress);

                RouteTarget? target;
                using (var scope = scopeFactory.CreateScope())
                {
                    var resolver = scope.ServiceProvider.GetRequiredService<IServerRouteResolver>();
                    target = await resolver.ResolveAsync(hostname, stoppingToken);
                }
                if (target is null) { logger.LogDebug("No route for host '{Host}'; dropping.", hostname); return; }

                backend = await TryConnectBackendAsync(target, stoppingToken);

                if (backend is null)
                {
                    // Server is down or still booting.
                    if (handshake.NextState == 1)
                    {
                        await SendSleepingStatusAsync(clientStream, target.DisplayName, handshake.ProtocolVersion, stoppingToken);
                    }
                    else if (handshake.NextState == 2)
                    {
                        await WakeAsync(target, stoppingToken);
                        await SendLoginDisconnectAsync(clientStream,
                            $"§e{target.DisplayName} is starting up.§r\nGive it a moment, then reconnect.", stoppingToken);
                    }
                    return;
                }

                var backendStream = backend.GetStream();
                await backendStream.WriteAsync(lengthResult.Raw, stoppingToken);
                await backendStream.WriteAsync(body.AsMemory(0, length), stoppingToken);
                await backendStream.FlushAsync(stoppingToken);

                logger.LogDebug("Routed '{Host}' -> {Target}:{Port} (state {State}).",
                    hostname, target.Host, target.Port, handshake.NextState);

                // Only login/play connections (state 2) count as players for idle tracking.
                if (handshake.NextState == 2)
                {
                    tracker.Opened(target.ServerId);
                    try { await PumpBothAsync(clientStream, backendStream, stoppingToken); }
                    finally { tracker.Closed(target.ServerId); }
                }
                else
                {
                    await PumpBothAsync(clientStream, backendStream, stoppingToken);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { logger.LogDebug(ex, "Connection handling failed."); }
            finally { backend?.Dispose(); }
        }

        private async Task<TcpClient?> TryConnectBackendAsync(RouteTarget target, CancellationToken stoppingToken)
        {
            var backend = new TcpClient { NoDelay = true };
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(TimeSpan.FromSeconds(_options.BackendConnectTimeoutSeconds));
                await backend.ConnectAsync(target.Host, target.Port, cts.Token);
                return backend;
            }
            catch
            {
                backend.Dispose();
                return null;
            }
        }

        /// <summary>Starts the server's container if it isn't already running.</summary>
        private async Task WakeAsync(RouteTarget target, CancellationToken stoppingToken)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var docker = scope.ServiceProvider.GetRequiredService<IDockerServiceResolver>().Resolve(null);
                var info = await docker.InspectContainerAsync(target.ContainerId, stoppingToken);
                if (info.State?.Running != true)
                {
                    logger.LogInformation("Waking server '{Name}' on connect.", target.DisplayName);
                    await docker.StartContainerAsync(target.ContainerId, stoppingToken);
                }
                tracker.Touch(target.ServerId); // reset the idle clock so we don't immediately re-sleep it
            }
            catch (Exception ex) { logger.LogDebug(ex, "Wake failed for '{Name}'.", target.DisplayName); }
        }

        private static async Task SendSleepingStatusAsync(NetworkStream client, string name, int protocol, CancellationToken ct)
        {
            // Read (and ignore) the client's Status Request, reply with our MOTD, then echo the Ping as Pong.
            var json = MinecraftProtocol.StatusJson($"§7{name} is asleep — join to start it.", protocol);
            try
            {
                await ReadPacketAsync(client, ct);                                   // status request (0x00)
                await client.WriteAsync(MinecraftProtocol.StatusResponsePacket(json), ct);
                await client.FlushAsync(ct);
                var ping = await ReadPacketAsync(client, ct);                        // ping (0x01 + long)
                if (ping is not null) { await client.WriteAsync(ping, ct); await client.FlushAsync(ct); }
            }
            catch { /* client went away - fine */ }
        }

        private static async Task SendLoginDisconnectAsync(NetworkStream client, string reason, CancellationToken ct)
        {
            try
            {
                await client.WriteAsync(MinecraftProtocol.LoginDisconnectPacket(reason), ct);
                await client.FlushAsync(ct);
            }
            catch { /* client went away - fine */ }
        }

        /// <summary>Reads one framed packet and returns its raw bytes (length prefix + body), or null on EOF.</summary>
        private static async Task<byte[]?> ReadPacketAsync(NetworkStream stream, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            var len = await MinecraftProtocol.ReadVarIntAsync(stream, cts.Token);
            if (len is null || len.Value <= 0 || len.Value > 65536) return null;
            var payload = new byte[len.Value];
            await stream.ReadExactlyAsync(payload.AsMemory(0, len.Value), cts.Token);
            return [.. len.Raw, .. payload];
        }

        private static async Task PumpBothAsync(NetworkStream a, NetworkStream b, CancellationToken stoppingToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var one = CopyAsync(a, b, linked.Token);
            var two = CopyAsync(b, a, linked.Token);
            await Task.WhenAny(one, two);
            linked.Cancel();
            await Task.WhenAll(
                one.ContinueWith(_ => { }, TaskScheduler.Default),
                two.ContinueWith(_ => { }, TaskScheduler.Default));
        }

        private static async Task CopyAsync(NetworkStream from, NetworkStream to, CancellationToken cancellationToken)
        {
            var buffer = new byte[16 * 1024];
            try
            {
                int read;
                while ((read = await from.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    await to.FlushAsync(cancellationToken);
                }
            }
            catch { /* peer closed or cancelled */ }
        }
    }
}
