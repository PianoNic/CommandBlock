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

        /// <summary>How long a joining player may be held in the login phase while their server boots, before
        /// giving up and asking them to reconnect. Kept generous because modded servers can take minutes; the
        /// client is pinged throughout so it won't time out on its own.</summary>
        public int MaxHoldSeconds { get; set; } = 180;

        /// <summary>How long a client has to send its handshake before it's dropped - keeps port
        /// scanners and half-open connections from tying up sockets.</summary>
        public int HandshakeTimeoutSeconds { get; set; } = 5;

        /// <summary>How long to wait when dialing a backend before treating it as down/asleep.</summary>
        public int BackendConnectTimeoutSeconds { get; set; } = 2;
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
        Limbo.LimboRegistry limboRegistry,
        Limbo.LimboSession limboSession,
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
            using var _lease = client;
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
                        var motd = target.WakeOnConnect
                            ? $"§e● §f{target.DisplayName} §7is sleeping\n§7Join to wake it up - starts automatically"
                            : $"§c● §f{target.DisplayName} §7is offline\n§8Wake-on-join is disabled";
                        await SendSleepingStatusAsync(clientStream, motd, handshake.ProtocolVersion, stoppingToken);
                    }
                    else if (handshake.NextState is 2 or 3)   // login, or a Transfer reconnect
                    {
                        if (!target.WakeOnConnect)
                        {
                            await SendLoginDisconnectAsync(clientStream, $"§7{target.DisplayName} is offline.", stoppingToken);
                            return;
                        }

                        // Wake in the background so the client isn't blocked on Docker starting the container -
                        // the limbo replays immediately and its readiness probe picks the backend up once it's live.
                        _ = WakeAsync(target, stoppingToken);

                        // Limbo: if we have a registry snapshot for this client's protocol, hold them in a live
                        // "starting" world and Transfer them back to the router once the backend is up (seamless
                        // auto-join, no manual reconnect). Otherwise fall through to the queue-then-kick.
                        var limbo = limboRegistry.Get(handshake.ProtocolVersion);
                        if (limbo is not null)
                        {
                            using var conn = tracker.Open(target.ServerId, RemoteAddress(client));
                            await limboSession.RunAsync(clientStream, limbo, handshake.ServerAddress, handshake.ServerPort,
                                async ct => { while (true) { var probe = await TryConnectBackendAsync(target, ct); if (probe is not null) { probe.Dispose(); return; } await Task.Delay(1000, ct); } },
                                stoppingToken);
                            return;
                        }

                        // Hold: stall the client in the login phase until the backend is up, then replay its login
                        // and pipe it straight in. We never interpret the game protocol here, so this covers every
                        // version and every mod loader - including Forge/NeoForge, which the limbo can never serve
                        // because it would have to impersonate their mod negotiation.
                        var (ready, loginStart) = await HoldForBackendAsync(clientStream, target, _options.MaxHoldSeconds, stoppingToken);
                        if (ready is not null)
                        {
                            backend = ready;
                            var readyStream = backend.GetStream();
                            await readyStream.WriteAsync(lengthResult.Raw, stoppingToken);
                            await readyStream.WriteAsync(body.AsMemory(0, length), stoppingToken);
                            if (loginStart is not null) await readyStream.WriteAsync(loginStart, stoppingToken);
                            await readyStream.FlushAsync(stoppingToken);
                            logger.LogInformation("Held '{Host}' through wake and spliced into {Target}.", hostname, target.DisplayName);
                            using var conn = tracker.Open(target.ServerId, RemoteAddress(client));
                            await PumpBothAsync(clientStream, readyStream, stoppingToken);
                            return;
                        }

                        await SendLoginDisconnectAsync(clientStream,
                            $"§e{target.DisplayName} is starting up.§r\nGive it a moment, then reconnect.", stoppingToken);
                    }
                    return;
                }

                var backendStream = backend.GetStream();
                if (handshake.NextState == 3)
                {
                    // A Transfer reconnect arrives with intent 3; backends reject that unless accepts-transfers
                    // is enabled, so rewrite it to a normal login (2) before piping. The client's buffered Login
                    // Start is then forwarded by the pump, so the backend sees an ordinary login.
                    await backendStream.WriteAsync(MinecraftProtocol.HandshakePacket(handshake.ProtocolVersion, handshake.ServerAddress, handshake.ServerPort, 2), stoppingToken);
                }
                else
                {
                    await backendStream.WriteAsync(lengthResult.Raw, stoppingToken);
                    await backendStream.WriteAsync(body.AsMemory(0, length), stoppingToken);
                }
                await backendStream.FlushAsync(stoppingToken);

                logger.LogDebug("Routed '{Host}' -> {Target}:{Port} (state {State}).",
                    hostname, target.Host, target.Port, handshake.NextState);

                // Only login/play connections (state 2) count as players for idle tracking and show
                // up in the Connections view. The handle closes the connection on dispose.
                if (handshake.NextState == 2)
                {
                    using var conn = tracker.Open(target.ServerId, RemoteAddress(client));
                    await PumpBothAsync(clientStream, backendStream, stoppingToken);
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

        private static string RemoteAddress(TcpClient client)
        {
            try { return (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown"; }
            catch { return "unknown"; }
        }

        /// <summary>Holds a joining client in the login phase until the backend accepts connections, returning it
        /// along with the client's captured Login Start so the caller can replay it and splice the connection
        /// through. The client is kept alive with periodic Login Plugin Requests - its login read-timeout is on
        /// inactivity rather than total duration - and its answers to those are consumed here so the backend never
        /// sees replies to requests it never sent. No game-protocol state is interpreted, which is exactly why this
        /// works for every version and every mod loader, including Forge/NeoForge.</summary>
        private async Task<(TcpClient? backend, byte[]? loginStart)> HoldForBackendAsync(
            NetworkStream clientStream, RouteTarget target, int maxSeconds, CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            byte[]? loginStart = null;

            var reader = Task.Run(async () =>
            {
                try
                {
                    while (!linked.Token.IsCancellationRequested)
                    {
                        var len = await MinecraftProtocol.ReadVarIntAsync(clientStream, linked.Token);
                        if (len is null || len.Value <= 0 || len.Value > 2_000_000) return;
                        var buf = new byte[len.Value];
                        await clientStream.ReadExactlyAsync(buf, linked.Token);
                        var pos = 0;
                        // Keep the raw Login Start frame to replay verbatim; drop plugin responses (0x02).
                        if (MinecraftProtocol.TryReadVarInt(buf, ref pos, out var id) && id == 0x00 && loginStart is null)
                            loginStart = [.. len.Raw, .. buf];
                    }
                }
                catch { /* client gone, or we cancelled */ }
            }, linked.Token);

            var deadline = Environment.TickCount64 + Math.Max(1, maxSeconds) * 1000L;
            var messageId = 1;
            var nextPing = Environment.TickCount64 + 3000;
            try
            {
                while (Environment.TickCount64 < deadline && !ct.IsCancellationRequested)
                {
                    var probe = await TryConnectBackendAsync(target, ct);
                    if (probe is not null) return (probe, loginStart);

                    if (Environment.TickCount64 >= nextPing)
                    {
                        try
                        {
                            await clientStream.WriteAsync(MinecraftProtocol.LoginPluginRequestPacket(messageId++, "commandblock:please_wait"), ct);
                            await clientStream.FlushAsync(ct);
                        }
                        catch { return (null, loginStart); }   // client hung up
                        nextPing = Environment.TickCount64 + 8000;
                    }
                    await Task.Delay(500, ct);
                }
                return (null, loginStart);
            }
            finally { linked.Cancel(); try { await reader; } catch { } }
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

        /// <summary>Polls the backend once a second (up to <paramref name="maxSeconds"/>) until it
        /// accepts connections, i.e. the server has finished booting. Returns the open connection, or
        /// null if it didn't come up in time.</summary>
        private async Task<TcpClient?> WaitForBackendAsync(RouteTarget target, int maxSeconds, CancellationToken stoppingToken)
        {
            var deadline = DateTime.UtcNow.AddSeconds(maxSeconds);
            while (DateTime.UtcNow < deadline && !stoppingToken.IsCancellationRequested)
            {
                var backend = await TryConnectBackendAsync(target, stoppingToken);
                if (backend is not null) return backend;
                try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
            return null;
        }

        /// <summary>Starts the server's container if it isn't already running.</summary>
        private async Task WakeAsync(RouteTarget target, CancellationToken stoppingToken)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var docker = scope.ServiceProvider.GetRequiredService<IDockerService>();
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

        private static async Task SendSleepingStatusAsync(NetworkStream client, string motd, int protocol, CancellationToken ct)
        {
            // Read (and ignore) the client's Status Request, reply with our MOTD, then echo the Ping as Pong.
            var json = MinecraftProtocol.StatusJson(motd, protocol);
            try
            {
                await ReadPacketAsync(client, ct);                                   // status request (0x00)
                await client.WriteAsync(MinecraftProtocol.StatusResponsePacket(json), ct);
                await client.FlushAsync(ct);
                var ping = await ReadPacketAsync(client, ct);                        // ping (0x01 + long)
                if (ping is not null) { await client.WriteAsync(ping, ct); await client.FlushAsync(ct); }
                await GracefulCloseAsync(client, ct);
            }
            catch { /* client went away - fine */ }
        }

        private static async Task SendLoginDisconnectAsync(NetworkStream client, string reason, CancellationToken ct)
        {
            try
            {
                await client.WriteAsync(MinecraftProtocol.LoginDisconnectPacket(reason), ct);
                await client.FlushAsync(ct);
                await GracefulCloseAsync(client, ct);
            }
            catch { /* client went away - fine */ }
        }

        /// <summary>Emits our FIN and drains whatever the client already pipelined before the socket is
        /// disposed. A real client sends its Login Start right behind the handshake; closing the socket with
        /// that still unread makes the OS send an RST instead of a FIN (RFC 1122 sec 4.2.2.13), and the reset
        /// races ahead of our disconnect packet - so the client shows "Connection reset" instead of the
        /// message. Draining first lets the close be a clean FIN. Best-effort, capped at 2s.</summary>
        private static async Task GracefulCloseAsync(NetworkStream client, CancellationToken stoppingToken)
        {
            try
            {
                client.Socket.Shutdown(SocketShutdown.Send); // FIN, after the already-flushed disconnect bytes
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                var sink = new byte[4096];
                while (await client.ReadAsync(sink, cts.Token) > 0) { } // discard the client's pipelined bytes
            }
            catch { /* already gone / drained / timed out - fine */ }
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
