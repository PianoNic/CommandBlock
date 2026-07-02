namespace CommandBlock.Application.Dtos.BackupSchedule
{
    public record BackupScheduleDto
    {
        public required Guid Id { get; init; }
        public required Guid ServerId { get; init; }
        public required string CronExpression { get; init; }
        public required bool Enabled { get; init; }
        public DateTime? NextRunAt { get; init; }
        public DateTime? LastRunAt { get; init; }
        public string? LastStatus { get; init; }
        public string? LastError { get; init; }
        public required DateTime CreatedAt { get; init; }
    }

    /// <summary>Request body for creating a schedule.</summary>
    public record CreateBackupScheduleDto
    {
        /// <summary>Standard 5-field cron, e.g. "0 3 * * *" (daily at 03:00 UTC).</summary>
        public required string CronExpression { get; init; }
    }

    /// <summary>Request body for enabling/disabling a schedule.</summary>
    public record ToggleBackupScheduleDto
    {
        public required bool Enabled { get; init; }
    }
}
