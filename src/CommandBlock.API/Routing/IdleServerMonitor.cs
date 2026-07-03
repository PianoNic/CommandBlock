using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.API.Routing
{
    /// <summary>Stops managed servers that have <c>AutoSleepEnabled</c> set once they've had no players
    /// (no live routed connections) for their configured idle window, so an unused server frees its RAM.
    /// Wake-on-connect brings them back. Auto-sleep is a per-server setting, so this always runs and
    /// simply skips servers that haven't opted in.</summary>
    public sealed class IdleServerMonitor(
        IServiceScopeFactory scopeFactory,
        IServerConnectionTracker tracker,
        TimeProvider time,
        ILogger<IdleServerMonitor> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Idle auto-sleep monitor running (per-server setting).");

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await SweepAsync(stoppingToken); }
                catch (Exception ex) { logger.LogDebug(ex, "Idle sweep failed."); }
            }
        }

        private async Task SweepAsync(CancellationToken ct)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CommandBlockDbContext>();
            var docker = scope.ServiceProvider.GetRequiredService<IDockerService>();
            var activity = scope.ServiceProvider.GetRequiredService<IActivityLogger>();

            var servers = await db.ServerInstances
                .AsNoTracking()
                .Where(s => s.IsManaged && s.ContainerId != null && s.AutoSleepEnabled)
                .Select(s => new { s.Id, s.ContainerId, s.ContainerName, s.DisplayName, s.ServerType, s.AutoSleepIdleMinutes })
                .ToListAsync(ct);
            if (servers.Count == 0) return;

            // One daemon call: which of our containers are actually running right now.
            var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in await docker.ListContainersAsync(all: false, ct))
                if (c.Names is not null)
                    foreach (var n in c.Names) running.Add(n.TrimStart('/'));

            var now = time.GetUtcNow().UtcDateTime;
            foreach (var s in servers)
            {
                if (s.ContainerName is null || !running.Contains(s.ContainerName)) continue; // already stopped
                if (tracker.ActiveCount(s.Id) > 0) continue;                                  // players on

                var idle = TimeSpan.FromMinutes(Math.Max(1, s.AutoSleepIdleMinutes));
                var last = tracker.LastActivity(s.Id);
                if (last is null)
                {
                    // Running but never seen by the tracker (e.g. freshly created). Seed the clock now
                    // so it can age out on a later sweep instead of being stopped immediately.
                    tracker.Touch(s.Id);
                    continue;
                }

                if (now - last.Value < idle) continue;

                logger.LogInformation("Auto-sleeping idle server '{Name}'.", s.DisplayName);
                try
                {
                    await docker.StopContainerAsync(s.ContainerId!, ct);
                    await activity.LogAsync("server.sleep", s.ContainerName, s.Id, s.ServerType, "idle", ct);
                }
                catch (Exception ex) { logger.LogDebug(ex, "Failed to stop idle server '{Name}'.", s.DisplayName); }
            }
        }
    }
}
