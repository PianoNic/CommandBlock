namespace CommandBlock.Domain
{
    /// <summary>A captured limbo replay snapshot for one Minecraft protocol version. The config + play byte
    /// sequences (gzipped in <see cref="Data"/>) are replayed to a joining client to spawn it in the limbo,
    /// and the per-version packet ids drive the keep-alive, the "starting" screen, and the Transfer.
    ///
    /// Captured on the fly by probing a running backend of that version (offline-login capture + RCON id
    /// sniffing) and keyed by <see cref="Protocol"/>, so the limbo works for every version actually run -
    /// not a hand-authored, hard-coded blob. A client whose protocol has no snapshot falls back to the kick.</summary>
    public class LimboSnapshot : BaseEntity
    {
        /// <summary>The Minecraft protocol version this snapshot serves (e.g. 775 = 26.1.2). Unique.</summary>
        public required int Protocol { get; set; }

        /// <summary>Version name from the backend's server-list ping (e.g. "26.1.2"), for display.</summary>
        public string? VersionName { get; set; }

        /// <summary>Gzipped JSON <c>{ config:[{id,hex}], play:[{id,hex}] }</c> - the decompressed config
        /// (registry) and play (join) packet bodies captured from a real backend, replayed verbatim (and
        /// uncompressed) to a matching-version client.</summary>
        public required byte[] Data { get; set; }

        // Clientbound Play packet ids captured for this version - they shift non-uniformly between versions,
        // so they're stored per snapshot rather than assumed. Screen ids are nullable (best-effort to sniff).
        public required int KeepAliveId { get; set; }
        public required int TransferId { get; set; }
        public int? BossBarId { get; set; }
        public int? TitleTextId { get; set; }
        public int? SubtitleId { get; set; }
        public int? TitleTimesId { get; set; }
        public int? SystemChatId { get; set; }
    }
}
