using CommandBlock.Application.Dtos.BackupSchedule;
using CommandBlock.Domain;

namespace CommandBlock.Application.Mappings.BackupSchedule
{
    public static class BackupScheduleMappings
    {
        public static BackupScheduleDto ToDto(this CommandBlock.Domain.BackupSchedule s) => new()
        {
            Id = s.Id,
            InstanceId = s.InstanceId,
            CronExpression = s.CronExpression,
            Description = s.Description,
            Enabled = s.Enabled,
            LastRunAt = s.LastRunAt,
            LastStatus = s.LastStatus,
            LastError = s.LastError,
            NextRunAt = s.NextRunAt,
            CreatedAt = s.CreatedAt,
        };
    }
}
