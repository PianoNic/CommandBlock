namespace CommandBlock.Application.Dtos.Server
{
    /// <summary>Request body for a server's wake-on-connect settings.</summary>
    public record UpdateWakeDto
    {
        public required bool WakeOnConnect { get; init; }
        /// <summary>Queue hold window in seconds (0 = ask the player to reconnect). Clamped to 0-28.</summary>
        public int WakeQueueSeconds { get; init; }
    }
}
