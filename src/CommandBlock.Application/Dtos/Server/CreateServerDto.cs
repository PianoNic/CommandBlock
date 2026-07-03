namespace CommandBlock.Application.Dtos.Server
{
    /// <summary>Request body for creating a Minecraft server.</summary>
    public record CreateServerDto
    {
        /// <summary>The itzg TYPE / loader, e.g. "PAPER", "FABRIC", "FORGE", "VANILLA", "MODRINTH".</summary>
        public required string ServerType { get; init; }
        public required string DisplayName { get; init; }
        /// <summary>Hostname players connect with, e.g. "smp.gaggao.com". Must be unique.</summary>
        public required string Hostname { get; init; }
        /// <summary>Java heap / container memory, e.g. "4G".</summary>
        public required string Memory { get; init; }
        /// <summary>Minecraft version for plain loaders. Ignored for modpack types.</summary>
        public string? Version { get; init; }
        /// <summary>Modrinth slug / .mrpack URL / CurseForge ref. Required for modpack types.</summary>
        public string? ModpackRef { get; init; }
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
