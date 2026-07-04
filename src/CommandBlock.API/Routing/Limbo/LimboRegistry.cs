using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace CommandBlock.API.Routing.Limbo
{
    /// <summary>Per-protocol clientbound packet ids the limbo emits. Reverse-engineered per MC version - the
    /// play-state id map shifts non-uniformly between versions, so these can't be assumed across protocols.</summary>
    public sealed record LimboIds(byte KeepAlive, byte SbKeepAlive, byte BossBar, byte TitleText, byte Subtitle, byte TitleTimes, byte SystemChat, byte Transfer);

    /// <summary>Cached limbo byte sequences + ids for one protocol version. Frames are network-ready and
    /// uncompressed (the captured payloads are stored decompressed and the limbo never negotiates compression).</summary>
    public sealed class LimboData
    {
        public required int Protocol { get; init; }
        public required IReadOnlyList<byte[]> ConfigFrames { get; init; }
        public required IReadOnlyList<byte[]> PlayFrames { get; init; }
        public required LimboIds Ids { get; init; }
    }

    /// <summary>Holds the captured registry + play byte sequences the limbo replays, keyed by protocol version.
    /// The bytes are captured from a real backend of that version (see the limbo prototype / limbo-protocol notes)
    /// so any matching-version client accepts them verbatim - no hand-authored NBT. For now a single embedded
    /// 26.1.2 (protocol 775) snapshot; later populated by an on-boot capture per version.</summary>
    public sealed class LimboRegistry
    {
        private readonly Dictionary<int, LimboData> _byProtocol = new();

        public LimboRegistry()
        {
            // 26.1.2 = protocol 775. Ids verified against a real client (see limbo-protocol-775 notes).
            TryLoad(775, "registry-775.json.gz", new LimboIds(
                KeepAlive: 0x2c, SbKeepAlive: 0x1c, BossBar: 0x09,
                TitleText: 0x72, Subtitle: 0x70, TitleTimes: 0x73, SystemChat: 0x79, Transfer: 0x81));
        }

        /// <summary>Limbo data for a client's protocol, or null when we have no snapshot for it (caller falls back to a kick).</summary>
        public LimboData? Get(int protocol) => _byProtocol.GetValueOrDefault(protocol);

        private void TryLoad(int protocol, string resource, LimboIds ids)
        {
            var json = ReadEmbedded(resource);
            if (json is null) return;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            _byProtocol[protocol] = new LimboData
            {
                Protocol = protocol,
                ConfigFrames = FrameAll(root.GetProperty("config")),
                PlayFrames = FrameAll(root.GetProperty("play")),
                Ids = ids,
            };
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
