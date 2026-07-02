namespace CommandBlock.Application.Dtos.Server
{
    /// <summary>Online players on a server, read on demand via RCON <c>list</c>.</summary>
    public record PlayerListDto
    {
        public required int Online { get; init; }
        public required int Max { get; init; }
        public required IReadOnlyList<string> Players { get; init; }

        /// <summary>False when the server is offline or RCON isn't ready yet (players is then empty).</summary>
        public required bool Reachable { get; init; }
    }
}
