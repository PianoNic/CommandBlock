using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Command.Backup;
using CommandBlock.Infrastructure;

namespace CommandBlock.API
{
    /// <summary>Runs due backup schedules. Every minute it finds enabled schedules whose NextRunAt
    /// has passed, triggers a backup for each, stamps the outcome, and computes the next occurrence.</summary>
    public sealed class BackupSchedulerHostedService(IServiceScopeFactory scopeFactory, ILogger<BackupSchedulerHostedService> logger)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
            // Run once shortly after startup, then on each tick.
            do
            {
                try { await RunDueAsync(stoppingToken); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { logger.LogError(ex, "Backup scheduler tick failed."); }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task RunDueAsync(CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CommandBlockDbContext>();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var now = DateTime.UtcNow;
            var due = await db.BackupSchedules
                .Where(s => s.Enabled && s.NextRunAt != null && s.NextRunAt <= now)
                .ToListAsync(cancellationToken);

            foreach (var sch in due)
            {
                try
                {
                    await mediator.Send(new CreateBackupCommand(sch.ServerId), cancellationToken);
                    sch.LastStatus = "ok";
                    sch.LastError = null;
                }
                catch (Exception ex)
                {
                    sch.LastStatus = "error";
                    sch.LastError = ex.Message;
                    logger.LogWarning(ex, "Scheduled backup failed for server {ServerId}.", sch.ServerId);
                }

                sch.LastRunAt = now;
                try { sch.NextRunAt = Cronos.CronExpression.Parse(sch.CronExpression).GetNextOccurrence(now, TimeZoneInfo.Utc); }
                catch { sch.NextRunAt = null; /* invalid cron somehow persisted - stop scheduling it */ }

                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
