namespace CommandBlock.Application.Dtos.Server
{
    /// <summary>The handful of server.properties settings most people actually change. Read from and
    /// merged back into the real file (other properties and comments are preserved).</summary>
    public record ServerPropertiesDto
    {
        /// <summary>False when server.properties doesn't exist yet (server never started).</summary>
        public required bool Available { get; init; }

        public string Motd { get; init; } = "";
        public int MaxPlayers { get; init; } = 20;
        public string Difficulty { get; init; } = "easy";     // peaceful | easy | normal | hard
        public string Gamemode { get; init; } = "survival";   // survival | creative | adventure | spectator
        public bool Pvp { get; init; } = true;
        public bool OnlineMode { get; init; } = true;
        public bool Whitelist { get; init; }
        public bool Hardcore { get; init; }
        public bool AllowFlight { get; init; }
        public bool EnableCommandBlock { get; init; }
        public int ViewDistance { get; init; } = 10;
        public int SpawnProtection { get; init; } = 16;
    }

    /// <summary>Request body for updating the curated server.properties settings.</summary>
    public record UpdateServerPropertiesDto
    {
        public string Motd { get; init; } = "";
        public int MaxPlayers { get; init; } = 20;
        public string Difficulty { get; init; } = "easy";
        public string Gamemode { get; init; } = "survival";
        public bool Pvp { get; init; } = true;
        public bool OnlineMode { get; init; } = true;
        public bool Whitelist { get; init; }
        public bool Hardcore { get; init; }
        public bool AllowFlight { get; init; }
        public bool EnableCommandBlock { get; init; }
        public int ViewDistance { get; init; } = 10;
        public int SpawnProtection { get; init; } = 16;
    }
}
