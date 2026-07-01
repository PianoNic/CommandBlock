using System.Text;
using System.Text.RegularExpressions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Dtos.Server;
using CommandBlock.Application.Mappings.Server;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Queries.Server
{
    public record ListServersQuery : IQuery<IReadOnlyList<ServerInstanceDto>>;

    public partial class ListServersQueryHandler(CommandBlockDbContext db, IDockerServiceResolver dockerResolver)
        : IQueryHandler<ListServersQuery, IReadOnlyList<ServerInstanceDto>>
    {
        public async ValueTask<IReadOnlyList<ServerInstanceDto>> Handle(ListServersQuery query, CancellationToken cancellationToken)
        {
            var rows = await db.ServerInstances
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync(cancellationToken);

            // Container state lives on whichever daemon owns it; query each distinct target once.
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

            var result = new List<ServerInstanceDto>(rows.Count);
            foreach (var s in rows)
            {
                var state = s.ContainerName is not null && stateByName.TryGetValue(s.ContainerName, out var st) ? st : null;

                int? online = null, max = null;
                if (state == "running" && s.ContainerId is not null)
                {
                    try
                    {
                        var docker = dockerResolver.Resolve(s.NodeId);
                        var raw = Encoding.UTF8.GetString(await docker.ExecCaptureAsync(s.ContainerId, new[] { "rcon-cli", "list" }, cancellationToken));
                        var m = PlayerCountRegex().Match(raw);
                        if (m.Success) { online = int.Parse(m.Groups[1].Value); max = int.Parse(m.Groups[2].Value); }
                    }
                    catch
                    {
                        // RCON not ready / server still booting - leave counts null.
                    }
                }

                result.Add(s.ToDto(state, online, max));
            }
            return result;
        }

        // Matches "There are 3 of a max of 20 players online" and the "3/20" shorthand.
        [GeneratedRegex(@"(\d+)\s*(?:of a max of|/)\s*(\d+)")]
        private static partial Regex PlayerCountRegex();
    }
}
