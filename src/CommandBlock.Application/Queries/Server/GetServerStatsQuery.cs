using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Command.Server;
using CommandBlock.Application.Dtos.Server;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Queries.Server
{
    /// <summary>Vitals for one server's detail page: CPU, memory, uptime, running build and MOTD.
    /// All but uptime come from the shared status service, so the page can't disagree with the list.</summary>
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

            // The shared status service already pings the server (player counts, build, MOTD) and samples
            // CPU/memory; reuse it so the detail page can't disagree with the list.
            var live = (await status.GetAllAsync(cancellationToken)).FirstOrDefault(s => s.Id == query.Id);

            DateTime? startedAt = null;
            // "starting" still means the container is up - uptime is meaningful (and most interesting)
            // while a server is booting, so don't wait for it to answer pings.
            if (server.ContainerId is not null && live?.State is "running" or "starting")
                startedAt = await docker.GetContainerStartedAtAsync(server.ContainerId, cancellationToken);

            return new ServerStatsDto
            {
                State = live?.State,
                CpuPercent = live?.CpuPercent,
                MemoryBytes = live?.MemoryBytes,
                // What the container is actually held to; the configured value is only the heap, so fall
                // back to the ceiling we would enforce rather than to -Xmx itself.
                MemoryLimitBytes = live?.MemoryLimitBytes ?? Nullable(ServerContainerSpec.ContainerMemoryLimitBytes(server.Memory)),
                StartedAt = startedAt,
                RunningVersion = live?.RunningVersion,
                Motd = live?.Motd,
                PlayersOnline = live?.PlayersOnline,
                PlayersMax = live?.PlayersMax,
            };
        }

        /// <summary>Zero means "couldn't parse the configured memory", which is no ceiling at all.</summary>
        private static long? Nullable(long bytes) => bytes > 0 ? bytes : null;
    }
}
