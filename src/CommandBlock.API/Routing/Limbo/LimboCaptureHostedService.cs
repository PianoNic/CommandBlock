using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CommandBlock.API.Routing.Limbo
{
    /// <summary>Sweeps running managed servers and captures a <c>LimboSnapshot</c> for any protocol version we
    /// don't have yet, so the limbo waiting-room works on every version actually hosted here rather than only the
    /// one blob embedded in the image. Capturing logs a probe player in and fires RCON commands, so it only runs
    /// against servers with nobody connected — the sweep skips servers with live routed connections and
    /// <see cref="LimboCaptureService"/> re-checks the server's own player count before probing.</summary>
    public sealed class LimboCaptureHostedService(
        IServiceScopeFactory scopeFactory,
        IServerConnectionTracker tracker,
        LimboCaptureService capture,
        ILogger<LimboCaptureHostedService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Limbo snapshot capture sweeper running.");

            // A crash mid-capture can strand a throwaway container; clear those before doing anything else.
            await capture.CleanupOrphansAsync(stoppingToken);

            // Let the app and any already-running servers settle, then sweep; after that, every 5 minutes.
            try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
            catch (OperationCanceledException) { return; }

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
            do
            {
                try { await SweepAsync(stoppingToken); }
                catch (Exception ex) { logger.LogDebug(ex, "Limbo capture sweep failed."); }
            }
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task SweepAsync(CancellationToken ct)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CommandBlockDbContext>();
            var docker = scope.ServiceProvider.GetRequiredService<IDockerService>();

            var servers = await db.ServerInstances
                .AsNoTracking()
                .Where(s => s.IsManaged && s.ContainerId != null && s.ContainerName != null)
                .Select(s => new { s.Id, s.ContainerId, s.ContainerName, s.Port, s.DisplayName })
                .ToListAsync(ct);
            if (servers.Count == 0) return;

            // One daemon call: which of our containers are actually running right now.
            var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in await docker.ListContainersAsync(all: false, ct))
                if (c.Names is not null)
                    foreach (var n in c.Names) running.Add(n.TrimStart('/'));

            foreach (var s in servers)
            {
                if (ct.IsCancellationRequested) return;
                if (!running.Contains(s.ContainerName!)) continue;   // not up, nothing to probe
                if (tracker.ActiveCount(s.Id) > 0) continue;         // someone is playing on it

                // No-ops unless this protocol is >= 1.20.5, not yet captured, and the server is empty.
                // A probe takes ~a minute, so stop after one capture and pick the rest up next sweep.
                if (await capture.CaptureAsync(s.ContainerId!, s.ContainerName!, s.Port, ct)) return;
            }
        }
    }
}
