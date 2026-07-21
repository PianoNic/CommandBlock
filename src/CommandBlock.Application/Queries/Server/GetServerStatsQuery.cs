using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Command.Server;
using CommandBlock.Application.Dtos.Server;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Queries.Server
{
    /// <summary>Vitals for one server's detail page: CPU, memory, uptime, running build and MOTD.
    /// Kept out of the list/stream because the CPU sample costs about a second.</summary>
    public record GetServerStatsQuery(Guid Id) : IQuery<ServerStatsDto>;

    public class GetServerStatsQueryHandler(
        CommandBlockDbContext db,
        IDockerService docker,
        IServerStatusService status) : IQueryHandler<GetServerStatsQuery, ServerStatsDto>
    {
        public async ValueTask<ServerStatsDto> Handle(GetServerStatsQuery query, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.AsNoTracking().FirstOrDefaultAsync(s => s.Id == query.Id, cancellationToken)
                ?? throw new ServerNotFoundException(query.Id);

            // The shared status service already pings the server (player counts, build, MOTD); reuse it so
            // the detail page can't disagree with the list.
            var live = (await status.GetAllAsync(cancellationToken)).FirstOrDefault(s => s.Id == query.Id);

            double? cpu = null;
            DateTime? startedAt = null;
            if (server.ContainerId is not null && live?.State == "running")
            {
                var cpuTask = docker.GetContainerCpuPercentAsync(server.ContainerId, cancellationToken);
                var startedTask = docker.GetContainerStartedAtAsync(server.ContainerId, cancellationToken);
                cpu = await cpuTask;
                startedAt = await startedTask;
            }

            return new ServerStatsDto
            {
                State = live?.State,
                CpuPercent = cpu,
                MemoryBytes = live?.MemoryBytes,
                MemoryLimitBytes = ParseMemoryBytes(server.Memory),
                StartedAt = startedAt,
                RunningVersion = live?.RunningVersion,
                Motd = live?.Motd,
                PlayersOnline = live?.PlayersOnline,
                PlayersMax = live?.PlayersMax,
            };
        }

        /// <summary>Turns the configured heap string ("4G", "2048M") into bytes so usage can be shown
        /// against its ceiling. Null when it can't be parsed.</summary>
        private static long? ParseMemoryBytes(string? memory)
        {
            if (string.IsNullOrWhiteSpace(memory)) return null;
            var text = memory.Trim();
            var unit = char.ToUpperInvariant(text[^1]);
            var digits = unit is 'G' or 'M' or 'K' ? text[..^1] : text;
            if (!double.TryParse(digits, System.Globalization.CultureInfo.InvariantCulture, out var value)) return null;
            return unit switch
            {
                'G' => (long)(value * 1024 * 1024 * 1024),
                'M' => (long)(value * 1024 * 1024),
                'K' => (long)(value * 1024),
                _ => (long)value,
            };
        }
    }
}
