using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Infrastructure.Services
{
    public partial class ServerStatusService(CommandBlockDbContext db, IDockerService docker, IMemoryCache cache) : IServerStatusService
    {
        public async Task<IReadOnlyList<ServerStatus>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var rows = await db.ServerInstances
                .AsNoTracking()
                .Select(s => new { s.Id, s.ContainerName, s.ContainerId, s.WakeOnConnect, s.AutoSleepEnabled, s.Version })
                .ToListAsync(cancellationToken);

            // Docker state per container in one daemon call. The status line ("Exited (137) 2 hours ago")
            // carries the exit code, which is what separates a crash from a deliberate stop.
            var stateByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var exitCodeByName = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var c in await docker.ListContainersAsync(all: true, cancellationToken))
                    if (c.Names is not null)
                        foreach (var n in c.Names)
                        {
                            var name = n.TrimStart('/');
                            stateByName[name] = c.State ?? "unknown";
                            var em = c.Status is null ? Match.Empty : ExitCodeRegex().Match(c.Status);
                            exitCodeByName[name] = em.Success ? int.Parse(em.Groups[1].Value) : null;
                        }
            }
            catch { /* daemon unreachable -> null state */ }

            // Probe every running server concurrently - the per-server Docker calls (mc-monitor +
            // stats) each take a few hundred ms, so doing them sequentially made list latency scale
            // with server count. Running them in parallel keeps it flat.
            var tasks = rows.Select(async r =>
            {
                var containerState = r.ContainerName is not null && stateByName.TryGetValue(r.ContainerName, out var st) ? st : null;
                if (containerState != "running" || r.ContainerId is null)
                {
                    var exitCode = r.ContainerName is not null && exitCodeByName.TryGetValue(r.ContainerName, out var ec) ? ec : null;
                    return new ServerStatus(r.Id, RefineStoppedState(containerState, exitCode, r.WakeOnConnect, r.AutoSleepEnabled), null, null, null);
                }

                var svc = docker;
                string state = "running";
                int? online = null, max = null;

                // The ping and the stats read are independent, so start the memory read first and let it
                // run alongside whichever ping this server needs.
                var memTask = svc.GetContainerMemoryBytesAsync(r.ContainerId, cancellationToken);
                string? version = null, motd = null;

                if (UsesLegacyPing(r.Version) && r.ContainerName is not null)
                {
                    // Too old for the modern ping - ask it the only way it understands, and not often. A
                    // pre-1.4 server writes a "lost connection" line for every socket that closes, ours
                    // included, so probing it on each 4-second status poll would scroll its console with our
                    // own health checks. Cached results keep that down to about once a minute.
                    var legacy = await CachedLegacyPingAsync(r.Id, r.ContainerName, cancellationToken);
                    if (legacy is not null) { online = legacy.Online; max = legacy.Max; motd = legacy.Motd; version = r.Version; }
                    else state = "starting";

                    return new ServerStatus(r.Id, state, online, max, await memTask, version, motd);
                }

                // mc-monitor (bundled in the itzg image) reads player counts via the silent server-list
                // ping - unlike `rcon-cli list` it opens no RCON connection, so it doesn't flood the
                // console. Output: "host:port : version=... online=0 max=20 motd='...'".
                var raw = await SafeExecMonitorAsync(svc, r.ContainerId, cancellationToken);
                var memoryBytes = await memTask;

                var m = raw is null ? Match.Empty : PlayerCountRegex().Match(raw);
                if (m.Success) { online = int.Parse(m.Groups[1].Value); max = int.Parse(m.Groups[2].Value); }
                else state = "starting"; // container up but the server isn't answering pings yet

                // Same line also carries the running build and MOTD - free to pick up here.
                if (raw is not null)
                {
                    var vm = VersionRegex().Match(raw);
                    if (vm.Success) version = vm.Groups[1].Value.Trim();
                    var mm = MotdRegex().Match(raw);
                    if (mm.Success) motd = mm.Groups[1].Value.Trim();
                }

                return new ServerStatus(r.Id, state, online, max, memoryBytes, version, motd);
            });

            return await Task.WhenAll(tasks);
        }

        /// <summary>Splits a stopped container into the three cases an operator cares about, which Docker
        /// reports identically as "exited": a server that auto-slept and will wake on join, one that was
        /// deliberately stopped, and one that died. 0/143 (SIGTERM) and 137 (SIGKILL after a stop timeout)
        /// all mean "we asked it to stop" - only other codes are treated as a crash, so a slow shutdown
        /// never raises a false alarm.</summary>
        private static string? RefineStoppedState(string? containerState, int? exitCode, bool wakeOnConnect, bool autoSleepEnabled)
        {
            if (containerState != "exited") return containerState;
            if (exitCode is not null and not 0 and not 143 and not 137) return "crashed";
            return wakeOnConnect || autoSleepEnabled ? "sleeping" : containerState;
        }

        private const int McPort = 25565;
        private static readonly TimeSpan LegacyPingTimeout = TimeSpan.FromSeconds(2);

        // A live count may lag by up to a minute on these servers; a quiet console is worth more than
        // second-accurate player numbers on a 2012 build. Failures are re-tried sooner so a server that's
        // still booting doesn't read as "starting" long after it's up.
        private static readonly TimeSpan LegacyPingCacheHit = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan LegacyPingCacheMiss = TimeSpan.FromSeconds(10);

        private async Task<LegacyServerPing.LegacyStatus?> CachedLegacyPingAsync(Guid serverId, string containerName, CancellationToken ct)
        {
            var key = $"legacy-ping:{serverId}";
            if (cache.TryGetValue(key, out LegacyServerPing.LegacyStatus? cached)) return cached;

            var result = await LegacyServerPing.TryPingAsync(containerName, McPort, LegacyPingTimeout, ct);
            cache.Set(key, result, result is null ? LegacyPingCacheMiss : LegacyPingCacheHit);
            return result;
        }

        private static async Task<string?> SafeExecMonitorAsync(IDockerService svc, string containerId, CancellationToken ct)
        {
            try { return Encoding.UTF8.GetString(await svc.ExecCaptureAsync(containerId, new[] { "mc-monitor", "status" }, ct)); }
            catch { return null; }
        }

        /// <summary>Versions that only answer the pre-1.4 server list ping. 1.3 and older predate the
        /// handshake-based ping entirely. Unknown or LATEST is treated as modern.</summary>
        internal static bool UsesLegacyPing(string? mcVersion)
        {
            if (string.IsNullOrWhiteSpace(mcVersion)) return false;
            var parts = mcVersion.Trim().Split('.', '-');
            if (!int.TryParse(parts[0], out var major) || major != 1) return false;
            return parts.Length >= 2 && int.TryParse(parts[1], out var minor) && minor <= 3;
        }

        [GeneratedRegex(@"online=(\d+)\s+max=(\d+)")]
        private static partial Regex PlayerCountRegex();

        // Lazy up to " online=" because the reported name can contain spaces ("Paper 26.1.2").
        [GeneratedRegex(@"version=(.*?)\s+online=")]
        private static partial Regex VersionRegex();

        [GeneratedRegex(@"motd='([^']*)'")]
        private static partial Regex MotdRegex();

        // Docker status lines read "Exited (137) 2 hours ago".
        [GeneratedRegex(@"Exited \((\d+)\)")]
        private static partial Regex ExitCodeRegex();
    }
}
