using CommandBlock.Application.Dtos.BackupSchedule;

namespace CommandBlock.Application.Mappings.BackupSchedule
{
    public static class BackupScheduleMappings
    {
        public static BackupScheduleDto ToDto(this CommandBlock.Domain.BackupSchedule s) => new()
        {
            Id = s.Id,
            ServerId = s.ServerId,
            CronExpression = s.CronExpression,
            Enabled = s.Enabled,
            NextRunAt = s.NextRunAt,
            LastRunAt = s.LastRunAt,
            LastStatus = s.LastStatus,
            LastError = s.LastError,
            CreatedAt = s.CreatedAt,
        };
    }
}
