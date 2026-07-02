namespace CommandBlock.Application.Dtos.Backup
{
    /// <summary>Request body for spinning up a new server from a full server backup.</summary>
    public record CreateServerFromBackupDto
    {
        public required string DisplayName { get; init; }
        /// <summary>Hostname for the new server, e.g. "restored.example.com". Must be unique.</summary>
        public required string Hostname { get; init; }
    }
}
