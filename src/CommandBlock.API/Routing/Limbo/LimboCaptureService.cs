using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using CommandBlock.Domain;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CommandBlock.API.Routing.Limbo
{
    /// <summary>Probes a running backend to capture a <see cref="LimboSnapshot"/> for its protocol version: an
    /// offline-login client records the config (registry) + play (join) byte sequences, then determines the
    /// per-version play packet ids - keep-alive from the periodic 9-byte packet, and transfer / boss-bar / title /
    /// chat via RCON-triggered packets. Automates, in C#, the manual capture done for the embedded 26.1.2 blob.
    /// Fires RCON titles at @a, so it should only run against a server with no players online.</summary>
    public sealed class LimboCaptureService(IServiceScopeFactory scopeFactory, IDockerService docker, ILogger<LimboCaptureService> logger)
    {
        private const string ProbeName = "CBLimboProbe";

        /// <summary>Captures + stores a snapshot for the server's protocol unless one already exists. Best-effort:
        /// logs and returns on any failure (the limbo just keeps kicking clients of that version).</summary>
        public async Task CaptureAsync(string containerId, string containerName, int port, CancellationToken ct)
        {
            try
            {
                var ver = await PingProtocolAsync(containerName, port, ct);
                if (ver is null) { logger.LogDebug("Limbo capture: no status ping from {Name}", containerName); return; }

                if (await HasSnapshotAsync(ver.Value.protocol, ct)) { logger.LogDebug("Limbo snapshot for protocol {P} already exists", ver.Value.protocol); return; }

                logger.LogInformation("Capturing limbo snapshot for {Name} (protocol {P} / {V})", containerName, ver.Value.protocol, ver.Value.name);
                var snap = await ProbeAsync(containerId, containerName, port, ver.Value.protocol, ver.Value.name, ct);
                if (snap is null) { logger.LogWarning("Limbo capture failed for {Name} (protocol {P})", containerName, ver.Value.protocol); return; }

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CommandBlockDbContext>();
                if (!await db.LimboSnapshots.AnyAsync(s => s.Protocol == snap.Protocol, ct))
                {
                    db.LimboSnapshots.Add(snap);
                    await db.SaveChangesAsync(ct);
                    logger.LogInformation("Stored limbo snapshot for protocol {P} (keepAlive=0x{K:x}, transfer=0x{T:x})", snap.Protocol, snap.KeepAliveId, snap.TransferId);
                }
            }
            catch (Exception ex) { logger.LogDebug(ex, "Limbo capture errored for {Name}", containerName); }
        }

        private async Task<bool> HasSnapshotAsync(int protocol, CancellationToken ct)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CommandBlockDbContext>();
            return await db.LimboSnapshots.AnyAsync(s => s.Protocol == protocol, ct);
        }

        // --- status ping: learn the protocol version ---
        private static async Task<(int protocol, string name)?> PingProtocolAsync(string host, int port, CancellationToken ct)
        {
            using var tcp = new TcpClient { NoDelay = true };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(6));
            await tcp.ConnectAsync(host, port, cts.Token);
            var s = tcp.GetStream();
            await s.WriteAsync(Handshake(770, host, (ushort)port, 1), cts.Token);   // any protocol works for a status ping
            await s.WriteAsync(Frame(0x00, []), cts.Token);                          // Status Request
            await s.FlushAsync(cts.Token);
            var lenv = await MinecraftProtocol.ReadVarIntAsync(s, cts.Token);
            if (lenv is null || lenv.Value <= 0 || lenv.Value > 4_000_000) return null;
            var buf = new byte[lenv.Value];
            await s.ReadExactlyAsync(buf, cts.Token);
            var pos = 0;
            MinecraftProtocol.TryReadVarInt(buf, ref pos, out _);                    // packet id 0x00
            MinecraftProtocol.TryReadVarInt(buf, ref pos, out var jsonLen);
            var json = Encoding.UTF8.GetString(buf, pos, Math.Min(jsonLen, buf.Length - pos));
            var mProto = System.Text.RegularExpressions.Regex.Match(json, "\"protocol\"\\s*:\\s*(\\d+)");
            var mName = System.Text.RegularExpressions.Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]*)\"");
            return mProto.Success ? (int.Parse(mProto.Groups[1].Value), mName.Success ? mName.Groups[1].Value : "?") : null;
        }

        // --- the login probe + id sniff ---
        private async Task<LimboSnapshot?> ProbeAsync(string containerId, string host, int port, int protocol, string versionName, CancellationToken ct)
        {
            using var tcp = new TcpClient { NoDelay = true };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(45));
            await tcp.ConnectAsync(host, port, cts.Token);
            var s = tcp.GetStream();

            await s.WriteAsync(Handshake(protocol, host, (ushort)port, 2), cts.Token);
            await s.WriteAsync(Frame(0x00, [.. EncStr(ProbeName), .. new byte[16]]), cts.Token);   // Login Start (before compression)
            await s.FlushAsync(cts.Token);

            var threshold = -1;
            while (true)   // Login: Set Compression (0x03) then Login Success (0x02)
            {
                var p = await ReadRawAsync(s, threshold, cts.Token);
                if (p is null) return null;
                if (p.Value.id == 0x00) { logger.LogDebug("Limbo probe: login disconnect"); return null; }
                if (p.Value.id == 0x03) threshold = ReadVarIntAt(p.Value.body, SkipId(p.Value.body));
                else if (p.Value.id == 0x02) break;
            }
            await s.WriteAsync(WriteComp(threshold, 0x03, []), cts.Token);   // Login Acknowledged (compressed from here)
            await s.WriteAsync(WriteComp(threshold, 0x00, [.. EncStr("en_us"), 8, .. EncVar(0), 1, 0x7f, .. EncVar(1), 0, 1, .. EncVar(0)]), cts.Token);  // Client Information
            await s.FlushAsync(cts.Token);

            var config = new List<byte[]>();
            while (true)   // Configuration: registry etc., empty Known Packs reply, stop at Finish Config
            {
                var p = await ReadRawAsync(s, threshold, cts.Token);
                if (p is null) return null;
                if (p.Value.id == 0x04) { await s.WriteAsync(WriteComp(threshold, 0x04, PayloadOf(p.Value.body)), cts.Token); await s.FlushAsync(cts.Token); continue; }
                config.Add(p.Value.body);
                if (p.Value.id == 0x0e) { await s.WriteAsync(WriteComp(threshold, 0x07, EncVar(0)), cts.Token); await s.FlushAsync(cts.Token); continue; }
                if (p.Value.id == 0x03 && p.Value.body.Length == 1) break;
            }
            await s.WriteAsync(WriteComp(threshold, 0x03, []), cts.Token);   // Acknowledge Finish Configuration -> Play
            await s.FlushAsync(cts.Token);

            // Play: a background loop reads whole packets; RCON sniffs correlate new ids to commands.
            var play = new List<byte[]>();
            var seen = new HashSet<int>();
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            var reader = Task.Run(async () =>
            {
                try { while (true) { var p = await ReadRawAsync(s, threshold, readCts.Token); if (p is null) break; lock (play) { play.Add(p.Value.body); seen.Add(p.Value.id); } } }
                catch { }
            });

            async Task<int?> Sniff(string command)
            {
                HashSet<int> before; lock (play) before = [.. seen];
                await Rcon(containerId, command, ct);
                await Task.Delay(1300, ct);
                lock (play) { var fresh = seen.Except(before).ToList(); return fresh.Count > 0 ? fresh[^1] : null; }
            }

            await Task.Delay(700, ct);
            int joinEnd; lock (play) joinEnd = play.Count;   // play[0..joinEnd] = the join sequence we replay

            var titleText = await Sniff("title @a title {\"text\":\"c\"}");
            var subtitle = await Sniff("title @a subtitle {\"text\":\"c\"}");
            var titleTimes = await Sniff("title @a times 1 1 1");
            var systemChat = await Sniff("tellraw @a {\"text\":\"c\"}");
            await Rcon(containerId, "bossbar add cbcap {\"text\":\"c\"}", ct);
            var bossBar = await Sniff("bossbar set cbcap players @a");

            int? keepAlive = null;   // the lone 9-byte (1-byte id + 8-byte long) packet the server sends ~every 15s
            var kaDeadline = Environment.TickCount64 + 20000;
            while (keepAlive is null && Environment.TickCount64 < kaDeadline)
            {
                await Task.Delay(1500, ct);
                lock (play)
                {
                    for (var i = joinEnd; i < play.Count; i++)
                    {
                        var pos = 0;
                        if (MinecraftProtocol.TryReadVarInt(play[i], ref pos, out var id) && play[i].Length - pos == 8) { keepAlive = id; break; }  // id + 8-byte long
                    }
                }
            }

            var transfer = await Sniff("transfer 0.0.0.0 1 @a");   // emits Transfer (and disconnects us)
            await Rcon(containerId, "bossbar remove cbcap", ct);
            readCts.Cancel();
            try { await reader; } catch { }

            if (keepAlive is null || transfer is null) { logger.LogWarning("Limbo capture incomplete (keepAlive={K} transfer={T}) for protocol {P}", keepAlive, transfer, protocol); return null; }

            List<byte[]> join; lock (play) join = play.GetRange(0, Math.Min(joinEnd, play.Count));
            return new LimboSnapshot
            {
                Protocol = protocol,
                VersionName = versionName,
                Data = GzipJson(protocol, config, join),
                KeepAliveId = keepAlive.Value,
                TransferId = transfer.Value,
                BossBarId = bossBar,
                TitleTextId = titleText,
                SubtitleId = subtitle,
                TitleTimesId = titleTimes,
                SystemChatId = systemChat,
            };
        }

        private async Task Rcon(string containerId, string command, CancellationToken ct)
        {
            try { await docker.ExecCaptureAsync(containerId, ["rcon-cli", command], ct); }
            catch (Exception ex) { logger.LogDebug(ex, "RCON '{Cmd}' failed", command); }
        }

        // --- compression-aware framing ---
        private static async Task<(int id, byte[] body)?> ReadRawAsync(NetworkStream s, int threshold, CancellationToken ct)
        {
            var lenv = await MinecraftProtocol.ReadVarIntAsync(s, ct);
            if (lenv is null || lenv.Value <= 0 || lenv.Value > 8_000_000) return null;
            var frame = new byte[lenv.Value];
            await s.ReadExactlyAsync(frame, ct);
            byte[] body;
            if (threshold >= 0)
            {
                var pos = 0;
                MinecraftProtocol.TryReadVarInt(frame, ref pos, out var dataLen);
                if (dataLen == 0) body = frame[pos..];
                else
                {
                    using var ms = new MemoryStream(frame, pos, frame.Length - pos);
                    using var z = new ZLibStream(ms, CompressionMode.Decompress);
                    body = new byte[dataLen];
                    var read = 0; while (read < dataLen) { var r = await z.ReadAsync(body.AsMemory(read), ct); if (r == 0) break; read += r; }
                }
            }
            else body = frame;
            var p = 0;
            if (!MinecraftProtocol.TryReadVarInt(body, ref p, out var id)) return null;
            return (id, body);   // body = [id..payload], decompressed
        }

        private static byte[] WriteComp(int threshold, int id, ReadOnlySpan<byte> payload)
        {
            byte[] data = [.. EncVar(id), .. payload];
            if (threshold < 0) return [.. EncVar(data.Length), .. data];
            byte[] inner = [.. EncVar(0), .. data];   // dataLen 0 = uncompressed (probe never exceeds the threshold)
            return [.. EncVar(inner.Length), .. inner];
        }

        private static byte[] GzipJson(int protocol, List<byte[]> config, List<byte[]> play)
        {
            var sb = new StringBuilder();
            sb.Append("{\"proto\":").Append(protocol).Append(",\"config\":[");
            AppendPackets(sb, config); sb.Append("],\"play\":[");
            AppendPackets(sb, play); sb.Append("]}");
            using var outMs = new MemoryStream();
            using (var gz = new GZipStream(outMs, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                gz.Write(bytes, 0, bytes.Length);
            }
            return outMs.ToArray();
        }

        private static void AppendPackets(StringBuilder sb, List<byte[]> packets)
        {
            for (var i = 0; i < packets.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var p = packets[i];
                var pos = 0; MinecraftProtocol.TryReadVarInt(p, ref pos, out var id);
                sb.Append("{\"id\":").Append(id).Append(",\"hex\":\"").Append(Convert.ToHexString(p).ToLowerInvariant()).Append("\"}");
            }
        }

        // --- small helpers ---
        private static byte[] EncVar(int v) => MinecraftProtocol.EncodeVarInt(v);
        private static byte[] EncStr(string str) { var u = Encoding.UTF8.GetBytes(str); return [.. EncVar(u.Length), .. u]; }
        private static byte[] Frame(int id, ReadOnlySpan<byte> payload) { byte[] body = [.. EncVar(id), .. payload]; return [.. EncVar(body.Length), .. body]; }
        private static byte[] Handshake(int proto, string addr, ushort port, int next) => Frame(0x00, [.. EncVar(proto), .. EncStr(addr), (byte)(port >> 8), (byte)(port & 0xff), .. EncVar(next)]);
        private static int SkipId(byte[] body) { var p = 0; MinecraftProtocol.TryReadVarInt(body, ref p, out _); return p; }
        private static int ReadVarIntAt(byte[] body, int at) { MinecraftProtocol.TryReadVarInt(body, ref at, out var v); return v; }
        private static byte[] PayloadOf(byte[] body) => body[SkipId(body)..];
    }
}
