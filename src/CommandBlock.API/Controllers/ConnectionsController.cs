using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CommandBlock.API.Routing;
using CommandBlock.Infrastructure;

namespace CommandBlock.API.Controllers
{
    /// <summary>A live player connection through the router.</summary>
    public sealed record ConnectionDto(Guid ServerId, string ServerName, string Hostname, string RemoteAddress, DateTime OpenedAt);

    [ApiController]
    [Route("api/[controller]")]
    public class ConnectionsController(IServerConnectionTracker tracker, CommandBlockDbContext db) : ControllerBase
    {
        /// <summary>Every play connection currently routed through the proxy, newest first, with the
        /// server it's routed to.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<ConnectionDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            var snapshot = tracker.Snapshot();
            if (snapshot.Count == 0) return Ok(Array.Empty<ConnectionDto>());

            var ids = snapshot.Select(c => c.ServerId).Distinct().ToList();
            var servers = await db.ServerInstances.AsNoTracking()
                .Where(s => ids.Contains(s.Id))
                .Select(s => new { s.Id, s.DisplayName, s.Hostname })
                .ToDictionaryAsync(s => s.Id, cancellationToken);

            var result = snapshot
                .Select(c =>
                {
                    servers.TryGetValue(c.ServerId, out var s);
                    return new ConnectionDto(c.ServerId, s?.DisplayName ?? "(unknown)", s?.Hostname ?? "", c.RemoteAddress, c.OpenedAt);
                })
                .OrderByDescending(c => c.OpenedAt)
                .ToList();

            return Ok(result);
        }
    }
}
