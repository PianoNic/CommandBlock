namespace CommandBlock.Application.Dtos.Backup
{
    public record BackupEntryDto
    {
        public required Guid Id { get; init; }
        public required Guid ServerId { get; init; }
        public required string FileName { get; init; }
        public required long SizeBytes { get; init; }
        /// <summary>"World" (world folder only) or "Server" (full dump; can create a new server).</summary>
        public required string Kind { get; init; }
        public required DateTime CreatedAt { get; init; }
    }
}
