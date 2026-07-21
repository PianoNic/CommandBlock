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
                .Select(s => new { s.Id, s.ContainerId, s.ContainerName, s.Port, s.DisplayName, s.Version })
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

                // Servers too old for the Transfer packet can never yield a usable snapshot. Skip them on
                // the configured version, before opening a socket: the capture probe would otherwise ping
                // them the modern way every sweep, which an old server answers by logging a protocol error
                // to its own console forever.
                if (!CouldSupportTransfer(s.Version)) continue;

                // No-ops unless this protocol is >= 1.20.5, not yet captured, and the server is empty.
                // A probe takes ~a minute, so stop after one capture and pick the rest up next sweep.
                if (await capture.CaptureAsync(s.ContainerId!, s.ContainerName!, s.Port, ct)) return;
            }
        }

        /// <summary>Whether a server could plausibly speak the Transfer packet (1.20.5+), judged from the
        /// version the operator configured. Unknown/LATEST is treated as new, letting the probe decide.</summary>
        internal static bool CouldSupportTransfer(string? mcVersion)
        {
            if (string.IsNullOrWhiteSpace(mcVersion)) return true;
            var v = mcVersion.Trim();
            if (v.Equals("LATEST", StringComparison.OrdinalIgnoreCase)) return true;

            var parts = v.Split('.', '-');
            if (!int.TryParse(parts[0], out var major)) return true;
            if (major >= 2) return true;                                    // 26.x year-based scheme
            if (parts.Length < 2 || !int.TryParse(parts[1], out var minor)) return true;
            if (minor != 20) return minor > 20;
            return parts.Length >= 3 && int.TryParse(parts[2], out var patch) && patch >= 5;
        }
    }
}
