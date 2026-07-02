using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Command.Server;
using CommandBlock.Application.Dtos.BackupSchedule;
using CommandBlock.Application.Mappings.BackupSchedule;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.BackupSchedule
{
    public record CreateBackupScheduleCommand(Guid ServerId, string CronExpression) : ICommand<BackupScheduleDto>;
    public record ToggleBackupScheduleCommand(Guid ScheduleId, bool Enabled) : ICommand<BackupScheduleDto>;
    public record DeleteBackupScheduleCommand(Guid ScheduleId) : ICommand;

    /// <summary>Thrown when a schedule id doesn't resolve. The API maps it to 404.</summary>
    public sealed class BackupScheduleNotFoundException(Guid id) : Exception($"Backup schedule '{id}' was not found.");

    internal static class Cron
    {
        /// <summary>Parses a standard 5-field cron, or throws a clean ArgumentException.</summary>
        public static Cronos.CronExpression Parse(string expr)
        {
            try { return Cronos.CronExpression.Parse((expr ?? "").Trim()); }
            catch (Exception) { throw new ArgumentException($"'{expr}' is not a valid cron expression (5 fields: minute hour day month weekday)."); }
        }

        public static DateTime? Next(Cronos.CronExpression cron) => cron.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc);
    }

    public class CreateBackupScheduleCommandHandler(CommandBlockDbContext db, IActivityLogger activity)
        : ICommandHandler<CreateBackupScheduleCommand, BackupScheduleDto>
    {
        public async ValueTask<BackupScheduleDto> Handle(CreateBackupScheduleCommand command, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.FirstOrDefaultAsync(s => s.Id == command.ServerId, cancellationToken)
                ?? throw new ServerNotFoundException(command.ServerId);

            var cron = Cron.Parse(command.CronExpression);
            var entry = new CommandBlock.Domain.BackupSchedule
            {
                ServerId = command.ServerId,
                CronExpression = command.CronExpression.Trim(),
                Enabled = true,
                NextRunAt = Cron.Next(cron),
            };
            db.BackupSchedules.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
            await activity.LogAsync("backup.schedule.add", server.ContainerName ?? server.DisplayName, server.Id, server.ServerType, entry.CronExpression, cancellationToken);
            return entry.ToDto();
        }
    }

    public class ToggleBackupScheduleCommandHandler(CommandBlockDbContext db)
        : ICommandHandler<ToggleBackupScheduleCommand, BackupScheduleDto>
    {
        public async ValueTask<BackupScheduleDto> Handle(ToggleBackupScheduleCommand command, CancellationToken cancellationToken)
        {
            var entry = await db.BackupSchedules.FirstOrDefaultAsync(s => s.Id == command.ScheduleId, cancellationToken)
                ?? throw new BackupScheduleNotFoundException(command.ScheduleId);

            entry.Enabled = command.Enabled;
            // Re-arm from now when enabling so a long-disabled schedule doesn't fire immediately.
            entry.NextRunAt = command.Enabled ? Cron.Next(Cron.Parse(entry.CronExpression)) : null;
            await db.SaveChangesAsync(cancellationToken);
            return entry.ToDto();
        }
    }

    public class DeleteBackupScheduleCommandHandler(CommandBlockDbContext db)
        : ICommandHandler<DeleteBackupScheduleCommand>
    {
        public async ValueTask<Unit> Handle(DeleteBackupScheduleCommand command, CancellationToken cancellationToken)
        {
            var entry = await db.BackupSchedules.FirstOrDefaultAsync(s => s.Id == command.ScheduleId, cancellationToken);
            if (entry is null) return Unit.Value;
            db.BackupSchedules.Remove(entry);
            await db.SaveChangesAsync(cancellationToken);
            return Unit.Value;
        }
    }
}
