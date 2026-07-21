namespace CommandBlock.Application.Dtos.Server
{
    /// <summary>Request body for creating a Minecraft server.</summary>
    public record CreateServerDto
    {
        /// <summary>The itzg TYPE / loader, e.g. "PAPER", "FABRIC", "FORGE", "VANILLA", "MODRINTH".</summary>
        public required string ServerType { get; init; }
        public required string DisplayName { get; init; }
        /// <summary>Hostname players connect with, e.g. "smp.gaggao.com". Must be unique. Required when
        /// <see cref="RoutedThroughProxy"/> is true, ignored otherwise.</summary>
        public string? Hostname { get; init; }

        /// <summary>True (the default) to reach the server through the router by hostname; false to publish
        /// its own host port instead. The two are exclusive.</summary>
        public bool RoutedThroughProxy { get; init; } = true;

        /// <summary>Host port to publish. Required when <see cref="RoutedThroughProxy"/> is false.</summary>
        public int? LanPort { get; init; }

        /// <summary>Host interface to bind the published port to. Empty binds every interface.</summary>
        public string? LanBindAddress { get; init; }
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
        /// <summary>Install the Via stack so clients of any Minecraft version can join (Paper-family and Fabric).</summary>
        public bool AllowAnyClientVersion { get; init; }
        /// <summary>Extra JVM options (JVM_OPTS).</summary>
        public string? JvmArgs { get; init; }
        /// <summary>Extra itzg env vars, one KEY=VALUE per line.</summary>
        public string? ExtraEnv { get; init; }
    }
}
