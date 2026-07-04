using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace CommandBlock.API.Routing.Limbo
{
    /// <summary>Drives a client through login -> configuration -> play using captured backend bytes, holds it in a
    /// "server is starting" screen with keep-alives, then Transfers it back to the router once the backend is up.
    /// Everything is uncompressed (captured payloads are stored decompressed; the limbo never sends Set Compression).</summary>
    public sealed class LimboSession(ILogger<LimboSession> logger)
    {
        /// <summary>Runs the limbo for one client. <paramref name="waitForBackendReady"/> completes when the backend
        /// can accept players; the client is then transferred back to <paramref name="reconnectHost"/>:<paramref name="reconnectPort"/>
        /// (the address it originally dialled), which the router routes into the now-live server.</summary>
        public async Task RunAsync(NetworkStream client, LimboData data, string reconnectHost, ushort reconnectPort,
            Func<CancellationToken, Task> waitForBackendReady, CancellationToken stoppingToken)
        {
            var ids = data.Ids;
            var t0 = Environment.TickCount64;

            var start = await ReadPacketAsync(client, stoppingToken);           // Login Start (0x00): name + uuid
            if (start is null || start.Value.id != 0x00) { logger.LogInformation("Limbo: client sent no Login Start"); return; }
            var name = ReadString(start.Value.payload);
            logger.LogInformation("Limbo '{Name}' connected; replaying {C} config + {P} play packets", name, data.ConfigFrames.Count, data.PlayFrames.Count);

            // Login Success (0x02): uuid + name + varint(0 properties). No trailing bool on 1.21.2+/26.x.
            await client.WriteAsync(Pkt(0x02, [.. OfflineUuid(name), .. EncStr(name), .. MinecraftProtocol.EncodeVarInt(0)]), stoppingToken);

            if (!await ReadUntilAsync(client, 0x03, stoppingToken)) { logger.LogInformation("Limbo '{Name}': no Login Ack", name); return; }

            foreach (var f in data.ConfigFrames) await client.WriteAsync(f, stoppingToken);  // brand/flags/known-packs/registry/tags/finish
            await client.FlushAsync(stoppingToken);

            if (!await ReadUntilAsync(client, 0x03, stoppingToken)) { logger.LogInformation("Limbo '{Name}': no Finish-Config Ack", name); return; }

            foreach (var f in data.PlayFrames) await client.WriteAsync(f, stoppingToken);    // Join Game + spawn -> client renders the world
            await client.FlushAsync(stoppingToken);

            await SendStartingScreenAsync(client, ids, stoppingToken);
            logger.LogInformation("Limbo '{Name}' spawned + held ({Ms}ms); waiting for backend", name, Environment.TickCount64 - t0);

            // Hold: keep-alive loop + drain the client's packets, until the backend is ready.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var keepAlive = KeepAliveLoopAsync(client, ids, linked.Token);
            var drain = DrainAsync(client, linked.Token);
            try { await waitForBackendReady(linked.Token); }
            catch (OperationCanceledException) { return; }
            finally { }

            // Transfer back to the router (the address the client dialled) -> it pipes into the now-live backend.
            logger.LogDebug("Limbo transferring '{Name}' to {Host}:{Port}", name, reconnectHost, reconnectPort);
            await client.WriteAsync(Pkt(ids.Transfer, [.. EncStr(reconnectHost), .. MinecraftProtocol.EncodeVarInt(reconnectPort)]), stoppingToken);
            await client.FlushAsync(stoppingToken);
            await Task.Delay(250, stoppingToken);
            linked.Cancel();
            try { await Task.WhenAll(keepAlive, drain); } catch { }
        }

        private async Task SendStartingScreenAsync(NetworkStream client, LimboIds ids, CancellationToken ct)
        {
            byte[] bar =
            [
                .. new byte[16],                                  // boss bar UUID (fixed)
                .. MinecraftProtocol.EncodeVarInt(0),            // action 0 = add
                .. NbtString("§eServer is starting…"),    // title
                0x3f, 0x80, 0x00, 0x00,                          // health 1.0
                .. MinecraftProtocol.EncodeVarInt(4),            // colour: yellow
                .. MinecraftProtocol.EncodeVarInt(0),            // no divisions
                0x00,                                            // flags
            ];
            await client.WriteAsync(Pkt(ids.BossBar, bar), ct);
            await client.WriteAsync(Pkt(ids.SystemChat, [.. NbtString("§eServer is starting — you'll be let in automatically."), 0x00]), ct);
            await client.WriteAsync(Pkt(ids.TitleTimes, [.. Be32(10), .. Be32(72000), .. Be32(20)]), ct);
            await client.WriteAsync(Pkt(ids.Subtitle, NbtString("§7Please wait")), ct);
            await client.WriteAsync(Pkt(ids.TitleText, NbtString("§eStarting…")), ct);
            await client.FlushAsync(ct);
        }

        private static async Task KeepAliveLoopAsync(NetworkStream client, LimboIds ids, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await client.WriteAsync(Pkt(ids.KeepAlive, RandomNumberGenerator.GetBytes(8)), ct);
                    await client.FlushAsync(ct);
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                }
            }
            catch { /* cancelled / client gone */ }
        }

        private static async Task DrainAsync(NetworkStream client, CancellationToken ct)
        {
            var buf = new byte[4096];
            try { while (await client.ReadAsync(buf, ct) > 0) { } } catch { }
        }

        // --- framing / reading (uncompressed) ---
        private static async Task<(int id, byte[] payload)?> ReadPacketAsync(NetworkStream s, CancellationToken ct)
        {
            var len = await MinecraftProtocol.ReadVarIntAsync(s, ct);
            if (len is null || len.Value <= 0 || len.Value > 2_000_000) return null;
            var buf = new byte[len.Value];
            await s.ReadExactlyAsync(buf, ct);
            var pos = 0;
            if (!MinecraftProtocol.TryReadVarInt(buf, ref pos, out var id)) return null;
            return (id, buf[pos..]);
        }

        private static async Task<bool> ReadUntilAsync(NetworkStream s, int wantId, CancellationToken ct)
        {
            for (var i = 0; i < 64; i++)
            {
                var p = await ReadPacketAsync(s, ct);
                if (p is null) return false;
                if (p.Value.id == wantId) return true;
            }
            return false;
        }

        private static byte[] Pkt(int id, ReadOnlySpan<byte> payload)
        {
            byte[] body = [.. MinecraftProtocol.EncodeVarInt(id), .. payload];
            return [.. MinecraftProtocol.EncodeVarInt(body.Length), .. body];
        }

        private static byte[] EncStr(string s) { var u = Encoding.UTF8.GetBytes(s); return [.. MinecraftProtocol.EncodeVarInt(u.Length), .. u]; }

        // Network text component as a nameless-root NBT TAG_String; renders § legacy colour codes.
        private static byte[] NbtString(string s) { var u = Encoding.UTF8.GetBytes(s); return [0x08, (byte)(u.Length >> 8), (byte)(u.Length & 0xff), .. u]; }

        private static byte[] Be32(int n) => [(byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n];

        private static string ReadString(byte[] payload)
        {
            var pos = 0;
            if (!MinecraftProtocol.TryReadVarInt(payload, ref pos, out var len) || len < 0 || pos + len > payload.Length) return "Player";
            return Encoding.UTF8.GetString(payload, pos, len);
        }

        private static byte[] OfflineUuid(string name)
        {
            var h = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
            h[6] = (byte)((h[6] & 0x0f) | 0x30);   // version 3
            h[8] = (byte)((h[8] & 0x3f) | 0x80);   // variant
            return h;
        }
    }
}
