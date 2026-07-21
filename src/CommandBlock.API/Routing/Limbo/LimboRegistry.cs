using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using CommandBlock.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CommandBlock.API.Routing.Limbo
{
    /// <summary>Per-protocol clientbound packet ids the limbo emits. Reverse-engineered per MC version - the
    /// play-state id map shifts non-uniformly between versions. Screen ids are nullable: the auto-capture sniffs
    /// them best-effort, and the limbo simply omits a screen element whose id it doesn't have.</summary>
    public sealed record LimboIds(byte KeepAlive, byte SbKeepAlive, byte? BossBar, byte? TitleText, byte? Subtitle, byte? TitleTimes, byte? SystemChat, byte Transfer);

    /// <summary>Cached limbo byte sequences + ids for one protocol version. Frames are network-ready and
    /// uncompressed (captured payloads are stored decompressed and the limbo never negotiates compression).</summary>
    public sealed class LimboData
    {
        public required int Protocol { get; init; }
        public required IReadOnlyList<byte[]> ConfigFrames { get; init; }
        public required IReadOnlyList<byte[]> PlayFrames { get; init; }
        public required LimboIds Ids { get; init; }

        /// <summary>Bytes the server appends to Login Success after the properties count. Empty before 26.2,
        /// which added a trailing 16-byte field - omit it and a real client drops the connection.</summary>
        public byte[] LoginSuccessTail { get; init; } = [];
    }

    /// <summary>Serves the limbo replay data for a client's protocol. Prefers a <c>LimboSnapshot</c> captured
    /// from a real backend (stored in the DB by <see cref="LimboCaptureService"/>); falls back to the embedded
    /// 26.1.2 (775) blob shipped with the image; returns null for anything else so the router kicks cleanly.
    /// Built data is cached in memory; <see cref="Invalidate"/> drops a protocol after a fresh capture.</summary>
    public sealed class LimboRegistry(IServiceScopeFactory scopeFactory)
    {
        private readonly ConcurrentDictionary<int, LimboData> _cache = new();
        private LimboData? _embedded775;
        private bool _embeddedLoaded;

        /// <summary>Limbo data for a client's protocol, or null when we have no snapshot for it.</summary>
        public LimboData? Get(int protocol)
        {
            if (_cache.TryGetValue(protocol, out var cached)) return cached;
            var data = LoadFromDb(protocol) ?? (protocol == 775 ? LoadEmbedded775() : null);
            if (data is not null) _cache[protocol] = data;
            return data;
        }

        /// <summary>Drop a protocol's cached data (e.g. after a fresh capture) so the next Get reloads it.</summary>
        public void Invalidate(int protocol) => _cache.TryRemove(protocol, out _);

        private LimboData? LoadFromDb(int protocol)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CommandBlockDbContext>();
                var snap = db.LimboSnapshots.AsNoTracking().FirstOrDefault(s => s.Protocol == protocol);
                if (snap is null) return null;
                using var doc = JsonDocument.Parse(Gunzip(snap.Data));
                return new LimboData
                {
                    Protocol = protocol,
                    ConfigFrames = FrameAll(doc.RootElement.GetProperty("config")),
                    PlayFrames = FrameAll(doc.RootElement.GetProperty("play")),
                    LoginSuccessTail = doc.RootElement.TryGetProperty("loginTail", out var lt) && lt.GetString() is { Length: > 0 } hex
                        ? Convert.FromHexString(hex) : [],
                    Ids = new LimboIds((byte)snap.KeepAliveId, 0, (byte?)snap.BossBarId, (byte?)snap.TitleTextId,
                        (byte?)snap.SubtitleId, (byte?)snap.TitleTimesId, (byte?)snap.SystemChatId, (byte)snap.TransferId),
                };
            }
            catch { return null; }
        }

        private LimboData? LoadEmbedded775()
        {
            if (_embeddedLoaded) return _embedded775;
            _embeddedLoaded = true;
            var json = ReadEmbedded("registry-775.json.gz");
            if (json is null) return null;
            using var doc = JsonDocument.Parse(json);
            _embedded775 = new LimboData
            {
                Protocol = 775,
                ConfigFrames = FrameAll(doc.RootElement.GetProperty("config")),
                PlayFrames = FrameAll(doc.RootElement.GetProperty("play")),
                // Verified against a real 26.1.2 client.
                Ids = new LimboIds(0x2c, 0x1c, 0x09, 0x72, 0x70, 0x73, 0x79, 0x81),
            };
            return _embedded775;
        }

        // Each captured packet's "hex" is the decompressed [packetId..payload]; frame it as [VarInt length][bytes].
        private static List<byte[]> FrameAll(JsonElement arr)
        {
            var list = new List<byte[]>(arr.GetArrayLength());
            foreach (var p in arr.EnumerateArray())
            {
                var raw = Convert.FromHexString(p.GetProperty("hex").GetString()!);
                list.Add([.. MinecraftProtocol.EncodeVarInt(raw.Length), .. raw]);
            }
            return list;
        }

        private static string Gunzip(byte[] gz)
        {
            using var ms = new MemoryStream(gz);
            using var z = new GZipStream(ms, CompressionMode.Decompress);
            using var r = new StreamReader(z);
            return r.ReadToEnd();
        }

        private static string? ReadEmbedded(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            var full = Array.Find(asm.GetManifestResourceNames(), n => n.EndsWith(name, StringComparison.Ordinal));
            if (full is null) return null;
            using var s = asm.GetManifestResourceStream(full)!;
            using Stream inner = full.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(s, CompressionMode.Decompress) : s;
            using var r = new StreamReader(inner);
            return r.ReadToEnd();
        }
    }
}
