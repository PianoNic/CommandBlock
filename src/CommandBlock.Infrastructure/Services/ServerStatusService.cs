using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Infrastructure.Services
{
    public partial class ServerStatusService(CommandBlockDbContext db, IDockerServiceResolver dockerResolver) : IServerStatusService
    {
        public async Task<IReadOnlyList<ServerStatus>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var rows = await db.ServerInstances
                .AsNoTracking()
                .Select(s => new { s.Id, s.ContainerName, s.ContainerId, s.NodeId })
                .ToListAsync(cancellationToken);

            // Docker state per container, one call per distinct node.
            var stateByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var nodeId in rows.Select(r => r.NodeId).Distinct())
            {
                try
                {
                    foreach (var c in await dockerResolver.Resolve(nodeId).ListContainersAsync(all: true, cancellationToken))
                        if (c.Names is not null)
                            foreach (var n in c.Names)
                                stateByName[n.TrimStart('/')] = c.State ?? "unknown";
                }
                catch { /* daemon/node unreachable -> null state */ }
            }

            var result = new List<ServerStatus>(rows.Count);
            foreach (var r in rows)
            {
                var docker = r.ContainerName is not null && stateByName.TryGetValue(r.ContainerName, out var st) ? st : null;
                string? state = docker;
                int? online = null, max = null;

                if (docker == "running" && r.ContainerId is not null)
                {
                    try
                    {
                        var raw = Encoding.UTF8.GetString(
                            await dockerResolver.Resolve(r.NodeId).ExecCaptureAsync(r.ContainerId, new[] { "rcon-cli", "list" }, cancellationToken));
                        var m = PlayerCountRegex().Match(raw);
                        if (m.Success) { state = "running"; online = int.Parse(m.Groups[1].Value); max = int.Parse(m.Groups[2].Value); }
                        else state = "starting"; // RCON up but odd output
                    }
                    catch
                    {
                        // Container runs but RCON isn't answering yet -> the server is still booting.
                        state = "starting";
                    }
                }

                result.Add(new ServerStatus(r.Id, state, online, max));
            }
            return result;
        }

        [GeneratedRegex(@"(\d+)\s*(?:of a max of|/)\s*(\d+)")]
        private static partial Regex PlayerCountRegex();
    }
}
