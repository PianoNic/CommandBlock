namespace CommandBlock.Application.Dtos.Server
{
    /// <summary>Request body for changing a server's runtime settings. Applying these recreates the
    /// container in place (the world data is preserved).</summary>
    public record UpdateServerRuntimeDto
    {
        /// <summary>Java heap / container memory, e.g. "4G".</summary>
        public required string Memory { get; init; }
        /// <summary>Java major version ("21"/"17"/"11"/"8"), or null to auto-derive from the version.</summary>
        public string? JavaVersion { get; init; }
        /// <summary>Apply Aikar's tuned GC flags (USE_AIKAR_FLAGS).</summary>
        public bool UseAikarFlags { get; init; }
        /// <summary>Extra JVM options (JVM_OPTS).</summary>
        public string? JvmArgs { get; init; }
        /// <summary>Extra itzg env vars, one KEY=VALUE per line.</summary>
        public string? ExtraEnv { get; init; }
    }
}
