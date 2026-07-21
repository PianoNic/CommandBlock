namespace CommandBlock.Application.Dtos.Server
{
    /// <summary>Live detail-page vitals for a single server. Separate from the list/stream status because
    /// CPU needs a two-sample delta from the daemon (~1s), which would slow every list refresh.</summary>
    public record ServerStatsDto
    {
        /// <summary>"running", "starting", "exited", or null when the container doesn't exist.</summary>
        public string? State { get; init; }
        /// <summary>Percent of a host core (can exceed 100 on multi-core work). Null if unavailable.</summary>
        public double? CpuPercent { get; init; }
        /// <summary>Resident memory, matching what `docker stats` reports.</summary>
        public long? MemoryBytes { get; init; }
        /// <summary>Configured container limit, so usage can be shown against its ceiling.</summary>
        public long? MemoryLimitBytes { get; init; }
        /// <summary>When the container was last started (UTC), for uptime.</summary>
        public DateTime? StartedAt { get; init; }
        /// <summary>The build the server reports on ping, e.g. "Paper 26.1.2".</summary>
        public string? RunningVersion { get; init; }
        /// <summary>The server's MOTD as shown in the server list.</summary>
        public string? Motd { get; init; }
        public int? PlayersOnline { get; init; }
        public int? PlayersMax { get; init; }
    }
}
