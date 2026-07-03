using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Infrastructure.Services
{
    public partial class ServerStatusService(CommandBlockDbContext db, IDockerService docker) : IServerStatusService
    {
        public async Task<IReadOnlyList<ServerStatus>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var rows = await db.ServerInstances
                .AsNoTracking()
                .Select(s => new { s.Id, s.ContainerName, s.ContainerId })
                .ToListAsync(cancellationToken);

            // Docker state per container in one daemon call.
            var stateByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var c in await docker.ListContainersAsync(all: true, cancellationToken))
                    if (c.Names is not null)
                        foreach (var n in c.Names)
                            stateByName[n.TrimStart('/')] = c.State ?? "unknown";
            }
            catch { /* daemon unreachable -> null state */ }

            // Probe every running server concurrently - the per-server Docker calls (mc-monitor +
            // stats) each take a few hundred ms, so doing them sequentially made list latency scale
            // with server count. Running them in parallel keeps it flat.
            var tasks = rows.Select(async r =>
            {
                var containerState = r.ContainerName is not null && stateByName.TryGetValue(r.ContainerName, out var st) ? st : null;
                if (containerState != "running" || r.ContainerId is null)
                    return new ServerStatus(r.Id, containerState, null, null, null);

                var svc = docker;
                string state = "running";
                int? online = null, max = null;

                // mc-monitor (bundled in the itzg image) reads player counts via the silent
                // server-list ping - unlike `rcon-cli list` it opens no RCON connection, so it
                // doesn't flood the console. Its ping and the stats read are independent, so run
                // them together. Output: "host:port : version=... online=0 max=20 motd='...'".
                var monitorTask = SafeExecMonitorAsync(svc, r.ContainerId, cancellationToken);
                var memTask = svc.GetContainerMemoryBytesAsync(r.ContainerId, cancellationToken);
                var raw = await monitorTask;
                var memoryBytes = await memTask;

                var m = raw is null ? Match.Empty : PlayerCountRegex().Match(raw);
                if (m.Success) { online = int.Parse(m.Groups[1].Value); max = int.Parse(m.Groups[2].Value); }
                else state = "starting"; // container up but the server isn't answering pings yet

                return new ServerStatus(r.Id, state, online, max, memoryBytes);
            });

            return await Task.WhenAll(tasks);
        }

        private static async Task<string?> SafeExecMonitorAsync(IDockerService svc, string containerId, CancellationToken ct)
        {
            try { return Encoding.UTF8.GetString(await svc.ExecCaptureAsync(containerId, new[] { "mc-monitor", "status" }, ct)); }
            catch { return null; }
        }

        [GeneratedRegex(@"online=(\d+)\s+max=(\d+)")]
        private static partial Regex PlayerCountRegex();
    }
}
