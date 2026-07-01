using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

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
    }

    /// <summary>
    /// A hostname-aware Minecraft (Java) reverse proxy. It listens on a single port, reads each
    /// client's handshake to learn the address the player typed, looks that up in the routing table,
    /// then becomes a transparent byte pipe to the matching backend server - so any number of servers
    /// are reachable through one public port, distinguished only by their hostname.
    /// </summary>
    public sealed class MinecraftRouter(
        IServiceScopeFactory scopeFactory,
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
                    try
                    {
                        client = await listener.AcceptTcpClientAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Accept failed; continuing.");
                        continue;
                    }

                    // Handle each connection independently; a slow or broken client must never block
                    // the accept loop. Failures are swallowed inside HandleClientAsync.
                    _ = HandleClientAsync(client, stoppingToken);
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        private static TcpListener CreateListener(int port)
        {
            // Prefer a dual-stack socket so both IPv4 and IPv6 clients land here; fall back to IPv4
            // only if the platform refuses dual mode.
            try
            {
                var listener = new TcpListener(IPAddress.IPv6Any, port);
                listener.Server.DualMode = true;
                return listener;
            }
            catch
            {
                return new TcpListener(IPAddress.Any, port);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken stoppingToken)
        {
            using var _ = client;
            client.NoDelay = true;

            TcpClient? backend = null;
            try
            {
                var clientStream = client.GetStream();

                // Bound the handshake read so scanners/half-open connections can't hold a socket.
                using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                handshakeCts.CancelAfter(TimeSpan.FromSeconds(_options.HandshakeTimeoutSeconds));

                var lengthResult = await MinecraftProtocol.ReadVarIntAsync(clientStream, handshakeCts.Token);
                if (lengthResult is null) return; // client hung up before sending anything

                var length = lengthResult.Value;
                // A real handshake is tiny; anything huge is junk (or a legacy/other protocol). Drop it.
                if (length <= 0 || length > 65536)
                {
                    logger.LogDebug("Dropping connection with implausible packet length {Length}.", length);
                    return;
                }

                var body = new byte[length];
                await clientStream.ReadExactlyAsync(body.AsMemory(0, length), handshakeCts.Token);

                var handshake = MinecraftProtocol.ParseHandshake(body);
                if (handshake is null)
                {
                    logger.LogDebug("Dropping connection with unparseable handshake.");
                    return;
                }

                var hostname = MinecraftProtocol.SanitizeAddress(handshake.ServerAddress);

                RouteTarget? target;
                using (var scope = scopeFactory.CreateScope())
                {
                    var resolver = scope.ServiceProvider.GetRequiredService<IServerRouteResolver>();
                    target = await resolver.ResolveAsync(hostname, stoppingToken);
                }

                if (target is null)
                {
                    logger.LogDebug("No route for host '{Host}'; dropping.", hostname);
                    return;
                }

                backend = new TcpClient { NoDelay = true };
                await backend.ConnectAsync(target.Host, target.Port, stoppingToken);
                var backendStream = backend.GetStream();

                // Replay the handshake verbatim (length prefix + body) so the backend sees exactly
                // what the client sent, then pipe both directions until either side closes.
                await backendStream.WriteAsync(lengthResult.Raw, stoppingToken);
                await backendStream.WriteAsync(body.AsMemory(0, length), stoppingToken);
                await backendStream.FlushAsync(stoppingToken);

                logger.LogDebug("Routed '{Host}' -> {Target}:{Port} (state {State}).",
                    hostname, target.Host, target.Port, handshake.NextState);

                await PumpBothAsync(clientStream, backendStream, stoppingToken);
            }
            catch (OperationCanceledException) { /* shutdown or handshake timeout */ }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Connection handling failed.");
            }
            finally
            {
                backend?.Dispose();
            }
        }

        private static async Task PumpBothAsync(NetworkStream a, NetworkStream b, CancellationToken stoppingToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var one = CopyAsync(a, b, linked.Token);
            var two = CopyAsync(b, a, linked.Token);
            await Task.WhenAny(one, two);
            // One direction ended (a peer closed); unblock the other so both tear down promptly.
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
            catch { /* peer closed or cancelled - the other pump handles teardown */ }
        }
    }
}
