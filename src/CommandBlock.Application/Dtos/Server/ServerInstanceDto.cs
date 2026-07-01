namespace CommandBlock.Application.Dtos.Server
{
    public record ServerInstanceDto
    {
        public required Guid Id { get; init; }
        /// <summary>The itzg TYPE / loader, e.g. "PAPER", "FABRIC", "MODRINTH".</summary>
        public required string ServerType { get; init; }
        /// <summary>Minecraft version, or null when a modpack dictates it.</summary>
        public string? Version { get; init; }
        /// <summary>Previous version stashed by the version-change flow; powers the Rollback button.</summary>
        public string? PreviousVersion { get; init; }
        /// <summary>Modrinth slug / .mrpack URL / CurseForge ref, set for modpack server types.</summary>
        public string? ModpackRef { get; init; }
        /// <summary>Java heap / container memory, e.g. "4G".</summary>
        public required string Memory { get; init; }
        /// <summary>User-picked human-readable name. Mutable via the rename endpoint.</summary>
        public required string DisplayName { get; init; }
        /// <summary>The hostname players connect with; the router's routing key.</summary>
        public required string Hostname { get; init; }
        public required int Port { get; init; }
        /// <summary>Null for externally-registered servers.</summary>
        public string? ContainerName { get; init; }
        /// <summary>False when this server was registered externally (no CommandBlock container).</summary>
        public required bool IsManaged { get; init; }
        /// <summary>True when the container publishes its own host port instead of sitting behind the router.</summary>
        public required bool IsPublic { get; init; }
        /// <summary>Container state from Docker ("running", "exited", "created", ...). Null when
        /// no container is associated or Docker couldn't be queried.</summary>
        public string? State { get; init; }
        /// <summary>True if owned by servers.yaml. The UI hides/disables mutation controls.</summary>
        public required bool IsConfigManaged { get; init; }
        /// <summary>The node this server runs on, or null for the control plane's local Docker.</summary>
        public Guid? NodeId { get; init; }
        public required DateTime CreatedAt { get; init; }
    }
}
