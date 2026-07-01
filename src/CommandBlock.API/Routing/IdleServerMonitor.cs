using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.API.Routing
{
    /// <summary>Stops managed servers that have had no players (no live routed connections) for the
    /// configured idle window, so an unused server frees its RAM. Wake-on-connect brings them back.
    /// Off unless <c>Router:AutoSleepEnabled</c> is set.</summary>
    public sealed class IdleServerMonitor(
        IServiceScopeFactory scopeFactory,
        IServerConnectionTracker tracker,
        IOptions<RouterOptions> options,
        TimeProvider time,
        ILogger<IdleServerMonitor> logger) : BackgroundService
    {
        private readonly RouterOptions _options = options.Value;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.AutoSleepEnabled)
            {
                logger.LogInformation("Idle auto-sleep disabled (Router:AutoSleepEnabled=false).");
                return;
            }

            var idle = TimeSpan.FromMinutes(Math.Max(1, _options.AutoSleepIdleMinutes));
            logger.LogInformation("Idle auto-sleep on: servers idle > {Minutes} min will be stopped.", _options.AutoSleepIdleMinutes);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await SweepAsync(idle, stoppingToken); }
                catch (Exception ex) { logger.LogDebug(ex, "Idle sweep failed."); }
            }
        }

        private async Task SweepAsync(TimeSpan idle, CancellationToken ct)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CommandBlockDbContext>();
            var docker = scope.ServiceProvider.GetRequiredService<IDockerServiceResolver>().Resolve(null);
            var activity = scope.ServiceProvider.GetRequiredService<IActivityLogger>();

            var servers = await db.ServerInstances
                .AsNoTracking()
                .Where(s => s.IsManaged && s.ContainerId != null)
                .Select(s => new { s.Id, s.ContainerId, s.ContainerName, s.DisplayName, s.ServerType })
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
