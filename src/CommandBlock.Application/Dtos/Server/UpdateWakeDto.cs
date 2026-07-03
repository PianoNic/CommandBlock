namespace CommandBlock.Application.Dtos.Server
{
    /// <summary>Request body for a server's wake-on-connect and auto-sleep settings.</summary>
    public record UpdateWakeDto
    {
        public required bool WakeOnConnect { get; init; }
        /// <summary>Queue hold window in seconds (0 = ask the player to reconnect). Clamped to 0-28.</summary>
        public int WakeQueueSeconds { get; init; }
        /// <summary>Stop the server automatically after AutoSleepIdleMinutes with no players.</summary>
        public bool AutoSleepEnabled { get; init; }
        /// <summary>Idle window in minutes before auto-sleep (min 1). Clamped to 1-1440.</summary>
        public int AutoSleepIdleMinutes { get; init; }
    }
}
