using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using CommandBlock.Application.Command.Server;
using CommandBlock.Domain;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;
using Docker.DotNet.Models;
using Microsoft.EntityFrameworkCore;

namespace CommandBlock.API.Routing.Limbo
{
    /// <summary>Probes a running backend to capture a <see cref="LimboSnapshot"/> for its protocol version: an
    /// offline-login client records the config (registry) + play (join) byte sequences, then determines the
    /// per-version play packet ids via a persistent RCON connection (fire a command carrying a distinctive marker,
    /// find the play packet whose bytes contain it) plus keep-alive by timing. Only protocol >= 766 (1.20.5) is
    /// captured - older versions have no Transfer packet, so the limbo can't auto-join them anyway. Fires RCON
    /// titles at @a, so it should only run against a server with no players online.</summary>
    public sealed partial class LimboCaptureService(IServiceScopeFactory scopeFactory, IDockerService docker, ILogger<LimboCaptureService> logger)
    {
        private const string ProbeName = "CBLimboProbe";
        private const int MinTransferProtocol = 766;   // 1.20.5 - first version with the Transfer packet
        private const string CaptureLabel = "commandblock.limbo-capture";

        /// <summary>Captures + stores a snapshot for the server's protocol unless one already exists or the
        /// version is too old to matter. Best-effort: logs and returns on any failure.</summary>
        public async Task<bool> CaptureAsync(string containerId, string containerName, int port, CancellationToken ct)
        {
            try
            {
                var ver = await PingProtocolAsync(containerName, port, ct);
                if (ver is null) { logger.LogDebug("Limbo capture: no status ping from {Name}", containerName); return false; }
                if (ver.Value.protocol < MinTransferProtocol) { logger.LogDebug("Limbo capture: {Name} is protocol {P} (< 1.20.5) - no Transfer packet, skipping", containerName, ver.Value.protocol); return false; }
                if (await HasSnapshotAsync(ver.Value.protocol, ct)) { logger.LogDebug("Limbo snapshot for protocol {P} already exists", ver.Value.protocol); return false; }
                // The probe fires RCON titles and a /transfer; never run it while anyone is on the server.
                if (ver.Value.online > 0) { logger.LogDebug("Limbo capture: {Name} has {N} player(s) online, deferring", containerName, ver.Value.online); return false; }

                var (isOnlineMode, password) = await ReadModeAndPasswordAsync(containerId, ct);

                // The probe logs in offline, so an online-mode server would demand encryption we can't satisfy.
                // The snapshot is per-protocol, not per-server, so capture the same version from a throwaway instead.
                if (isOnlineMode)
                {
                    var v = ExtractVersion(ver.Value.name);
                    if (v is null) { logger.LogDebug("Limbo capture: {Name} is online-mode and its version '{V}' is unparseable", containerName, ver.Value.name); return false; }
                    logger.LogInformation("{Name} is online-mode; capturing protocol {P} from a throwaway {V} server instead", containerName, ver.Value.protocol, v);
                    return await CaptureViaThrowawayAsync(v, ct);
                }

                if (password is null) { logger.LogWarning("Limbo capture: no rcon.password for {Name}", containerName); return false; }
                return await ProbeAndStoreAsync(containerName, port, password, ver.Value.protocol, ver.Value.name, ct);
            }
            catch (Exception ex) { logger.LogDebug(ex, "Limbo capture errored for {Name}", containerName); return false; }
        }

        /// <summary>Reads a backend's <c>online-mode</c> and <c>rcon.password</c> out of its server.properties.</summary>
        private async Task<(bool isOnlineMode, string? password)> ReadModeAndPasswordAsync(string containerId, CancellationToken ct)
        {
            var props = await ReadServerPropertiesAsync(containerId, ct);
            if (props is null) return (false, null);
            var om = OnlineMode().Match(props);
            var pw = RconPassword().Match(props);
            return (om.Success && om.Groups[1].Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase),
                    pw.Success && pw.Groups[1].Value.Trim().Length > 0 ? pw.Groups[1].Value.Trim() : null);
        }

        /// <summary>Runs the probe against a backend we can offline-login to, and stores the resulting snapshot.
        /// Shared by the direct path (an offline-mode server probed in place) and the throwaway path.</summary>
        private async Task<bool> ProbeAndStoreAsync(string host, int port, string password, int protocol, string versionName, CancellationToken ct)
        {
            logger.LogInformation("Capturing limbo snapshot for {Name} (protocol {P} / {V})", host, protocol, versionName);
            var snap = await ProbeAsync(host, port, password, protocol, versionName, ct);
            if (snap is null) { logger.LogWarning("Limbo capture failed for {Name} (protocol {P})", host, protocol); return false; }

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CommandBlockDbContext>();
            if (await db.LimboSnapshots.AnyAsync(x => x.Protocol == snap.Protocol, ct)) return false;   // raced with another capture
            db.LimboSnapshots.Add(snap);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Stored limbo snapshot for protocol {P} (keepAlive=0x{K:x}, transfer=0x{T:x})", snap.Protocol, snap.KeepAliveId, snap.TransferId);
            return true;
        }

        /// <summary>Captures a snapshot for <paramref name="version"/> by spinning up a throwaway offline vanilla
        /// server, probing it, then destroying it. This is how a version gets captured when the real servers running
        /// it are online-mode, which the offline probe can't authenticate against. The world is flat and mob-free so
        /// generation is quick and the packet-id sniff isn't drowned in entity noise.</summary>
        public async Task<bool> CaptureViaThrowawayAsync(string version, CancellationToken ct)
        {
            var cname = $"commandblock-limbocap-{Guid.NewGuid():N}"[..28];
            string? id = null;
            try
            {
                logger.LogInformation("Starting throwaway {Name} (vanilla {V}) to capture a limbo snapshot", cname, version);
                var created = await docker.CreateContainerAsync(new CreateContainerParameters
                {
                    Image = $"{ServerContainerSpec.Image}:{ServerContainerSpec.AutoJavaForMinecraft(version)}",
                    Name = cname,
                    Env =
                    [
                        "EULA=TRUE", "TYPE=VANILLA", "MEMORY=2G", $"VERSION={version}",
                        "ONLINE_MODE=FALSE",                                              // the probe logs in offline
                        "LEVEL_TYPE=FLAT",                                                // quick gen, minimal entity noise
                        "SPAWN_ANIMALS=FALSE", "SPAWN_MONSTERS=FALSE", "SPAWN_NPCS=FALSE",
                        "VIEW_DISTANCE=4", "SPAWN_PROTECTION=0",
                    ],
                    ExposedPorts = new Dictionary<string, EmptyStruct> { [$"{ServerContainerSpec.McPort}/tcp"] = default },
                    HostConfig = new HostConfig { RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No } },
                    Labels = new Dictionary<string, string> { [CaptureLabel] = "true" },
                }, ct);
                id = created.ID;
                await docker.StartContainerAsync(id, ct);

                // Wait for it to answer a status ping - jar download plus first world-gen can take minutes.
                (int protocol, string name, int online)? ver = null;
                var deadline = DateTimeOffset.UtcNow.AddMinutes(8);
                while (ver is null && DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    try { ver = await PingProtocolAsync(cname, ServerContainerSpec.McPort, ct); } catch { /* still booting */ }
                }
                if (ver is null) { logger.LogWarning("Throwaway capture server for {V} never came up", version); return false; }
                if (await HasSnapshotAsync(ver.Value.protocol, ct)) return false;   // captured meanwhile

                var (_, password) = await ReadModeAndPasswordAsync(id, ct);
                if (password is null) { logger.LogWarning("Throwaway capture server for {V} has no rcon.password", version); return false; }
                return await ProbeAndStoreAsync(cname, ServerContainerSpec.McPort, password, ver.Value.protocol, ver.Value.name, ct);
            }
            catch (Exception ex) { logger.LogWarning(ex, "Throwaway limbo capture for {V} failed", version); return false; }
            finally
            {
                if (id is not null)
                {
                    // Always tear it down, even if the capture threw - these are disposable by design.
                    try { await docker.RemoveContainerAsync(id, force: true, CancellationToken.None); logger.LogInformation("Removed throwaway capture server {Name}", cname); }
                    catch (Exception ex) { logger.LogWarning(ex, "Could not remove throwaway capture server {Name}", cname); }
                }
            }
        }

        /// <summary>Removes capture containers left behind by a previous run (e.g. a crash mid-capture).</summary>
        public async Task CleanupOrphansAsync(CancellationToken ct)
        {
            try
            {
                foreach (var c in await docker.ListContainersAsync(all: true, ct))
                {
                    if (c.Labels is null || !c.Labels.ContainsKey(CaptureLabel)) continue;
                    try { await docker.RemoveContainerAsync(c.ID, force: true, ct); logger.LogInformation("Removed orphaned capture container {Id}", c.ID[..12]); }
                    catch { /* best effort */ }
                }
            }
            catch { /* best effort */ }
        }

        /// <summary>Pulls a concrete Minecraft version out of a status-ping version name ("26.2", "Paper 1.21.5").</summary>
        public static string? ExtractVersion(string? versionName)
        {
            if (string.IsNullOrWhiteSpace(versionName)) return null;
            var m = VersionToken().Match(versionName);
            return m.Success ? m.Value : null;
        }

        private async Task<bool> HasSnapshotAsync(int protocol, CancellationToken ct)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CommandBlockDbContext>();
            return await db.LimboSnapshots.AnyAsync(s => s.Protocol == protocol, ct);
        }

        private async Task<string?> ReadServerPropertiesAsync(string containerId, CancellationToken ct)
        {
            try { return Encoding.UTF8.GetString(await docker.ExecCaptureAsync(containerId, ["cat", "/data/server.properties"], ct)); }
            catch { return null; }
        }

        // --- status ping: learn the protocol version ---
        private static async Task<(int protocol, string name, int online)?> PingProtocolAsync(string host, int port, CancellationToken ct)
        {
            using var tcp = new TcpClient { NoDelay = true };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(6));
            await tcp.ConnectAsync(host, port, cts.Token);
            var s = tcp.GetStream();
            await s.WriteAsync(Handshake(770, host, (ushort)port, 1), cts.Token);
            await s.WriteAsync(Frame(0x00, []), cts.Token);
            await s.FlushAsync(cts.Token);
            var lenv = await MinecraftProtocol.ReadVarIntAsync(s, cts.Token);
            if (lenv is null || lenv.Value <= 0 || lenv.Value > 4_000_000) return null;
            var buf = new byte[lenv.Value];
            await s.ReadExactlyAsync(buf, cts.Token);
            var pos = 0;
            MinecraftProtocol.TryReadVarInt(buf, ref pos, out _);
            MinecraftProtocol.TryReadVarInt(buf, ref pos, out var jsonLen);
            var json = Encoding.UTF8.GetString(buf, pos, Math.Min(jsonLen, buf.Length - pos));
            var mProto = ProtocolJson().Match(json);
            var mName = NameJson().Match(json);
            var mOnline = OnlineJson().Match(json);
            return mProto.Success
                ? (int.Parse(mProto.Groups[1].Value), mName.Success ? mName.Groups[1].Value : "?", mOnline.Success ? int.Parse(mOnline.Groups[1].Value) : 0)
                : null;
        }

        // --- the login probe + RCON id sniff ---
        private async Task<LimboSnapshot?> ProbeAsync(string host, int port, string rconPassword, int protocol, string versionName, CancellationToken ct)
        {
            using var tcp = new TcpClient { NoDelay = true };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(60));
            await tcp.ConnectAsync(host, port, cts.Token);
            var s = tcp.GetStream();

            await s.WriteAsync(Handshake(protocol, host, (ushort)port, 2), cts.Token);
            await s.WriteAsync(Frame(0x00, [.. EncStr(ProbeName), .. new byte[16]]), cts.Token);
            await s.FlushAsync(cts.Token);

            var threshold = -1;
            while (true)   // Login: Set Compression (0x03) then Login Success (0x02)
            {
                var p = await ReadRawAsync(s, threshold, cts.Token);
                if (p is null) return null;
                if (p.Value.id == 0x00) { logger.LogDebug("Limbo probe: login disconnect"); return null; }
                if (p.Value.id == 0x01) { logger.LogDebug("Limbo probe: server sent Encryption Request (online-mode), cannot offline-probe"); return null; }
                if (p.Value.id == 0x03) threshold = ReadVarIntAt(p.Value.body, SkipId(p.Value.body));
                else if (p.Value.id == 0x02) break;
            }
            await s.WriteAsync(WriteComp(threshold, 0x03, []), cts.Token);   // Login Acknowledged
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
            await s.WriteAsync(WriteComp(threshold, 0x03, []), cts.Token);   // Ack Finish Configuration -> Play
            await s.FlushAsync(cts.Token);

            // Play: a background loop reads whole packets (with timestamps); RCON content-marks correlate ids.
            var play = new List<byte[]>();
            var playT = new List<long>();
            var t0 = Environment.TickCount64;
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            var reader = Task.Run(async () =>
            {
                try { while (true) { var p = await ReadRawAsync(s, threshold, readCts.Token); if (p is null) break; lock (play) { play.Add(p.Value.body); playT.Add(Environment.TickCount64); } } }
                catch { }
            });

            using var rcon = await RconClient.ConnectAsync(host, 25575, rconPassword, ct);
            if (rcon is null) { logger.LogWarning("Limbo capture: RCON connect failed for {Host}", host); readCts.Cancel(); return null; }

            async Task<int?> Sniff(string command, byte[] marker)
            {
                int before; lock (play) before = play.Count;
                await rcon.ExecAsync(command, ct);
                await Task.Delay(900, ct);
                lock (play) { for (var i = before; i < play.Count; i++) if (Contains(play[i], marker)) { var pos = 0; MinecraftProtocol.TryReadVarInt(play[i], ref pos, out var id); return id; } }
                return null;
            }

            await Task.Delay(2000, ct);
            int joinEnd; lock (play) joinEnd = play.Count;

            var titleText = await Sniff("title @a title {\"text\":\"CBmkTITLE\"}", "CBmkTITLE"u8.ToArray());
            var subtitle = await Sniff("title @a subtitle {\"text\":\"CBmkSUB\"}", "CBmkSUB"u8.ToArray());
            var titleTimes = await Sniff("title @a times 111 222 333", [.. Be32(111), .. Be32(222), .. Be32(333)]);
            var systemChat = await Sniff("tellraw @a {\"text\":\"CBmkCHAT\"}", "CBmkCHAT"u8.ToArray());
            await rcon.ExecAsync("bossbar add cbcap {\"text\":\"CBmkBOSS\"}", ct); await Task.Delay(400, ct);
            var bossBar = await Sniff("bossbar set cbcap players @a", "CBmkBOSS"u8.ToArray());

            // Keep-alive: the server puts System.currentTimeMillis() in the payload, so the 8-byte packet whose
            // payload reads as "about now" in epoch millis identifies it outright - entity packets never do.
            // (Timing alone is unreliable: servers often send the first keep-alive immediately after join.)
            await Task.Delay(20000, ct);
            int? keepAlive = null;
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var eights = new Dictionary<int, long>();
            lock (play)
            {
                for (var i = 0; i < play.Count; i++)
                {
                    var pos = 0;
                    if (!MinecraftProtocol.TryReadVarInt(play[i], ref pos, out var id) || play[i].Length - pos != 8) continue;
                    if (keepAlive is null && Math.Abs(BinaryPrimitives.ReadInt64BigEndian(play[i].AsSpan(pos)) - nowMs) < 300_000) keepAlive = id;
                    var t = playT[i] - t0;
                    if (!eights.TryGetValue(id, out var mn) || t < mn) eights[id] = t;
                }
            }
            // Fallback for forks that send a counter rather than a timestamp: the 8-byte id that first shows up
            // well after the join burst.
            if (keepAlive is null)
            {
                long best = long.MaxValue;
                foreach (var (id, mn) in eights) if (mn > 3000 && Math.Abs(mn - 15000) < best) { best = Math.Abs(mn - 15000); keepAlive = id; }
            }

            // Target the probe by name, not @a: if someone slipped onto the server mid-probe, @a would
            // transfer THEM to a bogus address.
            var transfer = await Sniff($"transfer 88.88.88.88 34567 {ProbeName}", "88.88.88.88"u8.ToArray());
            await rcon.ExecAsync("bossbar remove cbcap", ct);
            readCts.Cancel(); try { await reader; } catch { }

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
            return (id, body);
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

        private static bool Contains(byte[] hay, byte[] needle)
        {
            if (needle.Length == 0 || hay.Length < needle.Length) return false;
            for (var i = 0; i <= hay.Length - needle.Length; i++)
            {
                var ok = true;
                for (var j = 0; j < needle.Length; j++) if (hay[i + j] != needle[j]) { ok = false; break; }
                if (ok) return true;
            }
            return false;
        }

        // --- small helpers ---
        private static byte[] EncVar(int v) => MinecraftProtocol.EncodeVarInt(v);
        private static byte[] EncStr(string str) { var u = Encoding.UTF8.GetBytes(str); return [.. EncVar(u.Length), .. u]; }
        private static byte[] Frame(int id, ReadOnlySpan<byte> payload) { byte[] body = [.. EncVar(id), .. payload]; return [.. EncVar(body.Length), .. body]; }
        private static byte[] Handshake(int proto, string addr, ushort port, int next) => Frame(0x00, [.. EncVar(proto), .. EncStr(addr), (byte)(port >> 8), (byte)(port & 0xff), .. EncVar(next)]);
        private static byte[] Be32(int n) => [(byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n];
        private static int SkipId(byte[] body) { var p = 0; MinecraftProtocol.TryReadVarInt(body, ref p, out _); return p; }
        private static int ReadVarIntAt(byte[] body, int at) { MinecraftProtocol.TryReadVarInt(body, ref at, out var v); return v; }
        private static byte[] PayloadOf(byte[] body) => body[SkipId(body)..];

        [GeneratedRegex(@"rcon\.password=(.*)")] private static partial Regex RconPassword();
        [GeneratedRegex(@"online-mode=(.*)")] private static partial Regex OnlineMode();
        [GeneratedRegex(@"\d+\.\d+(\.\d+)?")] private static partial Regex VersionToken();
        [GeneratedRegex("\"protocol\"\\s*:\\s*(\\d+)")] private static partial Regex ProtocolJson();
        [GeneratedRegex("\"name\"\\s*:\\s*\"([^\"]*)\"")] private static partial Regex NameJson();
        [GeneratedRegex("\"online\"\\s*:\\s*(\\d+)")] private static partial Regex OnlineJson();
    }
}
