using System.Net.Sockets;
using System.Text;

namespace CommandBlock.Infrastructure.Services
{
    /// <summary>The pre-1.4 "server list ping", for servers too old to answer the modern one.
    ///
    /// Everything up to 1.3 replies to a single <c>0xFE</c> byte with <c>0xFF</c>, a UTF-16 length, and
    /// <c>MOTD§online§max</c>. It's a different conversation from the handshake-based ping every current
    /// tool speaks - including mc-monitor, whose legacy mode implements the later 1.4-1.6 variant and gets
    /// its connection reset by a 1.2.5 server. Asking such a server the modern way logs a "Protocol error"
    /// on its console on every poll and tells us nothing, so we speak its dialect instead: the server is
    /// dialled directly over the shared Docker network, the same way the router reaches it.</summary>
    public static class LegacyServerPing
    {
        public sealed record LegacyStatus(string Motd, int Online, int Max);

        public static async Task<LegacyStatus?> TryPingAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                using var client = new TcpClient { NoDelay = true };
                await client.ConnectAsync(host, port, cts.Token);
                var stream = client.GetStream();

                await stream.WriteAsync(new byte[] { 0xFE }, cts.Token);
                await stream.FlushAsync(cts.Token);

                // 0xFF, then a big-endian UTF-16 character count, then the string itself.
                var header = new byte[3];
                await stream.ReadExactlyAsync(header, cts.Token);
                if (header[0] != 0xFF) return null;

                var chars = (header[1] << 8) | header[2];
                if (chars is <= 0 or > 2048) return null;

                var payload = new byte[chars * 2];
                await stream.ReadExactlyAsync(payload, cts.Token);

                var text = Encoding.BigEndianUnicode.GetString(payload);
                var parts = text.Split('§');   // section sign
                if (parts.Length < 3) return null;

                return int.TryParse(parts[^2], out var online) && int.TryParse(parts[^1], out var max)
                    ? new LegacyStatus(string.Join('§', parts[..^2]), online, max)
                    : null;
            }
            catch
            {
                return null;   // not up yet, or not a server that speaks this
            }
        }
    }
}
