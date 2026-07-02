namespace CommandBlock.Domain
{
    /// <summary>A recurring backup for a <see cref="ServerInstance"/>, defined by a cron expression.
    /// A hosted service ticks periodically, runs any enabled schedule whose <see cref="NextRunAt"/>
    /// is due, then stamps the outcome and computes the next occurrence.</summary>
    public class BackupSchedule : BaseEntity
    {
        public required Guid ServerId { get; init; }

        /// <summary>Standard 5-field cron (minute hour day month weekday), e.g. "0 3 * * *".</summary>
        public required string CronExpression { get; set; }

        public bool Enabled { get; set; } = true;

        /// <summary>When this schedule is next due to run (UTC). Recomputed after each run and when
        /// the cron/enabled state changes.</summary>
        public DateTime? NextRunAt { get; set; }

        public DateTime? LastRunAt { get; set; }

        /// <summary>Outcome of the last run: "ok" or "error". Null until it has run once.</summary>
        public string? LastStatus { get; set; }

        public string? LastError { get; set; }
    }
}
