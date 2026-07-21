using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CommandBlock.API.Routing;
using CommandBlock.Infrastructure;

namespace CommandBlock.API.Controllers
{
    /// <summary>A live player connection through the router.</summary>
    public sealed record ConnectionDto(Guid ServerId, string ServerName, string Hostname, string RemoteAddress, DateTime OpenedAt);

    /// <summary>Connections routed to one server over the reporting window.</summary>
    public sealed record ServerTrafficDto(Guid ServerId, string ServerName, int Connections, int ActiveNow);

    /// <summary>Joins in one clock hour, oldest bucket first - the shape behind the traffic sparkline.</summary>
    public sealed record TrafficBucketDto(DateTime HourUtc, int Connections);

    /// <summary>How wake-on-connect is performing: p50/p95 are what a joining player actually waits.</summary>
    public sealed record WakeStatsDto(int Total, int Failed, double? MedianSeconds, double? P95Seconds, double? SlowestSeconds);

    /// <summary>A finished session, for the history list.</summary>
    public sealed record RecentConnectionDto(string ServerName, string RemoteAddress, DateTime OpenedAt, double DurationSeconds);

    /// <summary>A join the router turned away, and why.</summary>
    public sealed record RejectionDto(string Reason, int Count);

    public sealed record ConnectionStatsDto(
        DateTime SinceUtc,
        int ActiveNow,
        int PeakConcurrent,
        int TotalConnections,
        int UniqueAddresses,
        double? LongestActiveSeconds,
        double? MedianSessionSeconds,
        IReadOnlyList<ServerTrafficDto> ByServer,
        IReadOnlyList<TrafficBucketDto> Traffic,
        WakeStatsDto Wakes,
        IReadOnlyList<RecentConnectionDto> Recent,
        IReadOnlyList<RejectionDto> Rejections);

    [ApiController]
    [Route("api/[controller]")]
    public class ConnectionsController(
        IServerConnectionTracker tracker,
        IRouterTelemetry telemetry,
        CommandBlockDbContext db) : ControllerBase
    {
        /// <summary>Every play connection currently routed through the proxy, newest first, with the
        /// server it's routed to.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<ConnectionDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            var snapshot = tracker.Snapshot();
            if (snapshot.Count == 0) return Ok(Array.Empty<ConnectionDto>());

            var names = await ServerNamesAsync(snapshot.Select(c => c.ServerId), cancellationToken);

            var result = snapshot
                .Select(c =>
                {
                    names.TryGetValue(c.ServerId, out var s);
                    return new ConnectionDto(c.ServerId, s.Name ?? "(unknown)", s.Hostname ?? "", c.RemoteAddress, c.OpenedAt);
                })
                .OrderByDescending(c => c.OpenedAt)
                .ToList();

            return Ok(result);
        }

        /// <summary>Router vitals since the process started: traffic, session lengths, wake timings and
        /// turned-away joins. Everything is derived from an in-memory rolling buffer, so it costs no storage
        /// and resets on restart - <see cref="ConnectionStatsDto.SinceUtc"/> says from when.</summary>
        [HttpGet("stats")]
        [ProducesResponseType(typeof(ConnectionStatsDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Stats(CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var telem = telemetry.Snapshot();
            var live = tracker.Snapshot();

            var names = await ServerNamesAsync(
                telem.Connections.Select(c => c.ServerId).Concat(live.Select(c => c.ServerId)), cancellationToken);
            string NameOf(Guid id) => names.TryGetValue(id, out var s) ? s.Name ?? "(deleted)" : "(deleted)";

            // Finished sessions plus the ones still open - a player who has been on for an hour should count
            // as traffic, not appear only once they leave.
            var byServer = telem.Connections.Select(c => c.ServerId).Concat(live.Select(c => c.ServerId))
                .GroupBy(id => id)
                .Select(g => new ServerTrafficDto(g.Key, NameOf(g.Key), g.Count(), live.Count(c => c.ServerId == g.Key)))
                .OrderByDescending(s => s.Connections)
                .ToList();

            var openedAt = telem.Connections.Select(c => c.OpenedAt).Concat(live.Select(c => c.OpenedAt)).ToList();
            var thisHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
            var traffic = Enumerable.Range(0, 24)
                .Select(i => thisHour.AddHours(i - 23))
                .Select(h => new TrafficBucketDto(h, openedAt.Count(t => t >= h && t < h.AddHours(1))))
                .ToList();

            var durations = telem.Connections.Select(c => c.DurationSeconds).ToList();
            var wakeTimes = telem.Wakes.Where(w => w.Succeeded).Select(w => w.SecondsToReady).ToList();

            var stats = new ConnectionStatsDto(
                SinceUtc: telem.SinceUtc,
                ActiveNow: live.Count,
                PeakConcurrent: telem.PeakConcurrent,
                TotalConnections: telem.Connections.Count + live.Count,
                UniqueAddresses: telem.Connections.Select(c => c.RemoteAddress)
                    .Concat(live.Select(c => c.RemoteAddress)).Distinct().Count(),
                LongestActiveSeconds: live.Count == 0 ? null : live.Max(c => (now - c.OpenedAt).TotalSeconds),
                MedianSessionSeconds: Percentile(durations, 0.5),
                ByServer: byServer,
                Traffic: traffic,
                Wakes: new WakeStatsDto(
                    telem.Wakes.Count,
                    telem.Wakes.Count(w => !w.Succeeded),
                    Percentile(wakeTimes, 0.5),
                    Percentile(wakeTimes, 0.95),
                    wakeTimes.Count == 0 ? null : wakeTimes.Max()),
                Recent: telem.Connections
                    .OrderByDescending(c => c.ClosedAt)
                    .Take(25)
                    .Select(c => new RecentConnectionDto(NameOf(c.ServerId), c.RemoteAddress, c.OpenedAt, c.DurationSeconds))
                    .ToList(),
                Rejections: telem.Rejections
                    .Select(r => new RejectionDto(r.Key, r.Value))
                    .OrderByDescending(r => r.Count)
                    .ToList());

            return Ok(stats);
        }

        private async Task<Dictionary<Guid, (string? Name, string? Hostname)>> ServerNamesAsync(
            IEnumerable<Guid> serverIds, CancellationToken cancellationToken)
        {
            var ids = serverIds.Distinct().ToList();
            if (ids.Count == 0) return [];

            return await db.ServerInstances.AsNoTracking()
                .Where(s => ids.Contains(s.Id))
                .Select(s => new { s.Id, s.DisplayName, s.Hostname })
                .ToDictionaryAsync(s => s.Id, s => ((string?)s.DisplayName, (string?)s.Hostname), cancellationToken);
        }

        /// <summary>Nearest-rank percentile. Small samples are the norm here (a handful of wakes), where
        /// interpolating between two neighbours would invent a number no server ever actually took.</summary>
        private static double? Percentile(IReadOnlyList<double> values, double fraction)
        {
            if (values.Count == 0) return null;
            var sorted = values.OrderBy(v => v).ToList();
            var rank = (int)Math.Ceiling(fraction * sorted.Count) - 1;
            return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
        }
    }
}
