using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Dtos.Server;
using CommandBlock.Application.Mappings.Server;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Queries.Server
{
    public record ListServersQuery : IQuery<IReadOnlyList<ServerInstanceDto>>;

    public class ListServersQueryHandler(CommandBlockDbContext db, IDockerServiceResolver dockerResolver)
        : IQueryHandler<ListServersQuery, IReadOnlyList<ServerInstanceDto>>
    {
        public async ValueTask<IReadOnlyList<ServerInstanceDto>> Handle(ListServersQuery query, CancellationToken cancellationToken)
        {
            var rows = await db.ServerInstances
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync(cancellationToken);

            // Container state lives on whichever daemon owns it: the local one for control-plane
            // servers, the node's for node-hosted ones. Query each distinct target once (a name ->
            // state map) so a row without a container - or an offline node - just gets a null state.
            var stateByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var nodeId in rows.Select(s => s.NodeId).Distinct())
            {
                try
                {
                    var containers = await dockerResolver.Resolve(nodeId).ListContainersAsync(all: true, cancellationToken);
                    foreach (var c in containers)
                    {
                        if (c.Names is null) continue;
                        foreach (var raw in c.Names)
                            stateByName[raw.TrimStart('/')] = c.State ?? "unknown";
                    }
                }
                catch
                {
                    // Daemon/node unreachable - those rows return State=null and the UI shows "unknown".
                }
            }

            return rows.Select(s =>
            {
                var state = s.ContainerName is not null && stateByName.TryGetValue(s.ContainerName, out var st) ? st : null;
                return s.ToDto(state);
            }).ToList();
        }
    }
}
