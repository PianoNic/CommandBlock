using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace CommandBlock.API.Routing.Limbo
{
    /// <summary>Minimal Source RCON client - connect, authenticate once, then fire commands with ~0 latency.
    /// The limbo capture probe uses it to trigger packets (title/bossbar/transfer) so the response lands in a
    /// tight window, unlike a fresh <c>docker exec rcon-cli</c> per command (which adds ~1-2s and desyncs sniffing).</summary>
    public sealed class RconClient : IDisposable
    {
        private readonly TcpClient _tcp;
        private readonly NetworkStream _s;
        private int _id = 100;

        private RconClient(TcpClient tcp) { _tcp = tcp; _s = tcp.GetStream(); }

        /// <summary>Connects + authenticates, or null on failure (bad password / unreachable).</summary>
        public static async Task<RconClient?> ConnectAsync(string host, int port, string password, CancellationToken ct)
        {
            var tcp = new TcpClient { NoDelay = true };
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(6));
                await tcp.ConnectAsync(host, port, cts.Token);
                var c = new RconClient(tcp);
                await c.WriteAsync(1, 3, password, cts.Token);          // 3 = SERVERDATA_AUTH
                var (id, _) = await c.ReadAsync(cts.Token);             // auth response: id == 1 ok, -1 fail
                if (id != 1) { c.Dispose(); return null; }
                return c;
            }
            catch { tcp.Dispose(); return null; }
        }

        /// <summary>Fires a command and drains its response (fire-and-forget from the caller's view).</summary>
        public async Task ExecAsync(string command, CancellationToken ct)
        {
            await WriteAsync(_id++, 2, command, ct);                    // 2 = SERVERDATA_EXECCOMMAND
            try { using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(TimeSpan.FromSeconds(3)); await ReadAsync(cts.Token); } catch { }
        }

        private async Task WriteAsync(int id, int type, string body, CancellationToken ct)
        {
            var b = Encoding.ASCII.GetBytes(body);
            var pkt = new byte[14 + b.Length];                         // 4 len + 4 id + 4 type + body + 2 null
            BinaryPrimitives.WriteInt32LittleEndian(pkt, 10 + b.Length);
            BinaryPrimitives.WriteInt32LittleEndian(pkt.AsSpan(4), id);
            BinaryPrimitives.WriteInt32LittleEndian(pkt.AsSpan(8), type);
            b.CopyTo(pkt, 12);
            await _s.WriteAsync(pkt, ct);
            await _s.FlushAsync(ct);
        }

        private async Task<(int id, string body)> ReadAsync(CancellationToken ct)
        {
            var lenBuf = new byte[4];
            await _s.ReadExactlyAsync(lenBuf, ct);
            var len = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
            if (len < 10 || len > 8192) return (-2, "");
            var buf = new byte[len];
            await _s.ReadExactlyAsync(buf, ct);
            return (BinaryPrimitives.ReadInt32LittleEndian(buf), Encoding.ASCII.GetString(buf, 8, len - 10));
        }

        public void Dispose() => _tcp.Dispose();
    }
}
