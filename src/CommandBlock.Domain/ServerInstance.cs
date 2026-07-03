namespace CommandBlock.Domain
{
    /// <summary>A Minecraft server managed by CommandBlock. Each instance maps to one
    /// <c>itzg/minecraft-server</c> container. The container is reached internally by the router
    /// over the shared Docker network (by <see cref="ContainerName"/> on <see cref="Port"/>); it
    /// does not publish a host port unless <see cref="IsPublic"/> is set. Players connect through
    /// the router using <see cref="Hostname"/>, which the router matches against the address in the
    /// Minecraft handshake and forwards to the right container.</summary>
    public class ServerInstance : BaseEntity
    {
        /// <summary>The <c>itzg/minecraft-server</c> TYPE that selects the loader/installer, e.g.
        /// "VANILLA", "PAPER", "PURPUR", "FABRIC", "QUILT", "FORGE", "NEOFORGE", "SPIGOT",
        /// or a modpack installer type ("MODRINTH", "CURSEFORGE", "FTBA"). Immutable after create;
        /// switching loaders means a new server.</summary>
        public required string ServerType { get; init; }

        /// <summary>Minecraft version the server runs, e.g. "1.21.1", or "LATEST". Null when the
        /// version is dictated by a modpack (<see cref="ModpackRef"/>) rather than chosen directly.</summary>
        public string? Version { get; set; }

        /// <summary>A Modrinth modpack slug, a <c>.mrpack</c> URL, or a CurseForge project ref,
        /// set when <see cref="ServerType"/> is a modpack installer. The container downloads and
        /// installs the server side of the pack on first boot. Null for plain loaders.</summary>
        public string? ModpackRef { get; set; }

        /// <summary>Java heap / container memory allocation passed as the itzg MEMORY env, e.g.
        /// "4G". Mutable via the edit endpoint (applied on the next restart).</summary>
        public required string Memory { get; set; }

        /// <summary>Java major version the container runs, e.g. "21", "17", "8". Selects the itzg
        /// image tag (java runtime is chosen by tag, not env). Null = auto-derive from the Minecraft
        /// version. Changing it requires recreating the container.</summary>
        public string? JavaVersion { get; set; }

        /// <summary>When true, passes <c>USE_AIKAR_FLAGS=true</c> so itzg applies Aikar's tuned GC
        /// flags - the community-standard preset for most servers.</summary>
        public bool UseAikarFlags { get; set; }

        /// <summary>Free-form extra JVM options passed as <c>JVM_OPTS</c> (e.g. custom -XX/-D flags).
        /// Null/empty leaves the JVM defaults (plus Aikar flags if enabled).</summary>
        public string? JvmArgs { get; set; }

        /// <summary>Power-user escape hatch: extra <c>itzg/minecraft-server</c> environment variables,
        /// one <c>KEY=VALUE</c> per line. Applied last so they can override the derived env. Covers
        /// any itzg feature we don't surface as a first-class field.</summary>
        public string? ExtraEnv { get; set; }

        /// <summary>Human-readable name set by the user at create time. Mutable via the rename
        /// endpoint. Required - the UI always has a name to render.</summary>
        public required string DisplayName { get; set; }

        /// <summary>The hostname players connect to, e.g. "smp.gaggao.com". The router keys its
        /// routing table on this value; it must be unique across managed servers. Immutable after
        /// create (changing it would break existing player bookmarks and the routing map).</summary>
        public required string Hostname { get; init; }

        /// <summary>The Minecraft port inside the container (25565 by default). The router dials the
        /// container on this port over the shared network; it is not the public port.</summary>
        public int Port { get; init; } = 25565;

        /// <summary>Null for externally-registered servers - CommandBlock didn't create a container
        /// for them, so lifecycle/upgrade operations don't apply.</summary>
        public string? ContainerName { get; set; }

        /// <summary>Null for externally-registered servers, and until the container is first created.</summary>
        public string? ContainerId { get; set; }

        /// <summary>True for CommandBlock-provisioned containers, false for externally-registered
        /// servers. Drives whether start/stop/backup/version-change operations are available.</summary>
        public bool IsManaged { get; init; } = true;

        /// <summary>True when the container binds its own host port and is reachable directly,
        /// bypassing the router. Defaults to false: managed servers sit on the internal network and
        /// are reached only through the router, so the VPS exposes a single public port.</summary>
        public bool IsPublic { get; set; }

        /// <summary>True when the instance is owned by servers.yaml. Mutation endpoints reject
        /// changes so the declared config remains the source of truth. Cleared automatically on
        /// startup when the entry is removed from the file - then the user can clean it up via the UI.</summary>
        public bool IsConfigManaged { get; set; }

        /// <summary>The node this server's container runs on, or null when it runs on the control
        /// plane's local Docker daemon. When set, all container operations are dispatched to that
        /// node over SignalR (the node executes them locally and returns the result).</summary>
        public Guid? NodeId { get; set; }

        /// <summary>A user-uploaded server icon, cropped/resized to a 64x64 PNG (the Minecraft
        /// server-icon size). Shown in the UI; null falls back to the platform icon.</summary>
        public byte[]? IconPng { get; set; }

        /// <summary>When true, a player joining this server while it's stopped starts its container
        /// (wake-on-connect). Off by default - a per-server setting, read live by the router.</summary>
        public bool WakeOnConnect { get; set; }

        /// <summary>When waking on connect, hold the joining player up to this many seconds and let
        /// them straight in once the server is ready (a queue). 0 = ask them to reconnect. The router
        /// caps the effective wait below the client's ~30s login timeout.</summary>
        public int WakeQueueSeconds { get; set; }
    }
}
