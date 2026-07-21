using Docker.DotNet;
using Docker.DotNet.Models;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Infrastructure.Services
{
    public class DockerService(IDockerClient client) : IDockerService
    {
        public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await client.System.PingAsync(cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
        {
            var version = await client.System.GetVersionAsync(cancellationToken);
            return version.Version;
        }

        public Task<IList<ContainerListResponse>> ListContainersAsync(bool all = true, CancellationToken cancellationToken = default)
        {
            return client.Containers.ListContainersAsync(new ContainersListParameters { All = all }, cancellationToken);
        }

        public Task<ContainerInspectResponse> InspectContainerAsync(string id, CancellationToken cancellationToken = default)
        {
            return client.Containers.InspectContainerAsync(id, cancellationToken);
        }

        public async Task<long> GetHostMemoryTotalBytesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var info = await client.System.GetSystemInfoAsync(cancellationToken);
                return info.MemTotal;
            }
            catch { return 0; }
        }

        public async Task<long?> GetContainerMemoryBytesAsync(string id, CancellationToken cancellationToken = default)
        {
            try
            {
                // OneShot + Stream=false returns a single snapshot immediately. Without OneShot the
                // daemon samples over a ~1s window (to compute CPU deltas) - we only need memory, so
                // OneShot avoids that per-container second of latency.
                ContainerStatsResponse? snap = null;
                var progress = new Progress<ContainerStatsResponse>(r => snap ??= r);
                await client.Containers.GetContainerStatsAsync(id, new ContainerStatsParameters { Stream = false, OneShot = true }, progress, cancellationToken);
                if (snap?.MemoryStats is null) return null;

                // `docker stats` reports usage minus the reclaimable page cache; mirror that so the
                // number matches what users see elsewhere.
                var usage = (long)snap.MemoryStats.Usage;
                if (snap.MemoryStats.Stats is { } st && st.TryGetValue("inactive_file", out var inactive))
                    usage -= (long)inactive;
                return usage < 0 ? 0 : usage;
            }
            catch { return null; }
        }

        public async Task<double?> GetContainerCpuPercentAsync(string id, CancellationToken cancellationToken = default)
        {
            try
            {
                // CPU percent is a delta between two samples, so this deliberately does NOT use OneShot:
                // the daemon samples over ~1s and fills precpu_stats. That second of latency is why this
                // is kept off the list/stream path and only used where one server is being inspected.
                ContainerStatsResponse? snap = null;
                var progress = new Progress<ContainerStatsResponse>(r => snap ??= r);
                await client.Containers.GetContainerStatsAsync(id, new ContainerStatsParameters { Stream = false }, progress, cancellationToken);
                if (snap?.CPUStats is null || snap.PreCPUStats is null) return null;

                var cpuDelta = (double)snap.CPUStats.CPUUsage.TotalUsage - snap.PreCPUStats.CPUUsage.TotalUsage;
                var systemDelta = (double)snap.CPUStats.SystemUsage - snap.PreCPUStats.SystemUsage;
                if (cpuDelta <= 0 || systemDelta <= 0) return 0;

                var cpus = snap.CPUStats.OnlineCPUs > 0
                    ? snap.CPUStats.OnlineCPUs
                    : (uint)(snap.CPUStats.CPUUsage.PercpuUsage?.Count ?? 1);
                return Math.Round(cpuDelta / systemDelta * cpus * 100.0, 1);
            }
            catch { return null; }
        }

        public async Task<DateTime?> GetContainerStartedAtAsync(string id, CancellationToken cancellationToken = default)
        {
            try
            {
                var info = await client.Containers.InspectContainerAsync(id, cancellationToken);
                // The daemon reports this as an RFC3339 string, and uses a zero date for "never started".
                if (info?.State?.StartedAt is not string raw || raw.Length == 0) return null;
                if (!DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var started))
                    return null;
                return started.Year > 1 ? started : null;
            }
            catch { return null; }
        }

        public Task PullImageAsync(string image, string tag = "latest", CancellationToken cancellationToken = default)
        {
            return client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = image, Tag = tag },
                authConfig: null,
                progress: new Progress<JSONMessage>(),
                cancellationToken: cancellationToken);
        }

        public async Task<CreateContainerResponse> CreateContainerAsync(CreateContainerParameters parameters, CancellationToken cancellationToken = default)
        {
            var result = await client.Containers.CreateContainerAsync(parameters, cancellationToken);
            // Join the new container to CommandBlock's own Docker network(s) so CommandBlock can reach it by name on
            // its internal port. A containerized CommandBlock can't reach a private (127.0.0.1-bound) host
            // port, but it CAN reach the container directly over a shared user-defined network.
            await AttachToOwnNetworksAsync(result.ID, cancellationToken);
            return result;
        }

        // CommandBlock's user-defined networks, detected once. Empty when CommandBlock runs on the host (desktop /
        // dev) or only on the default bridge - those cases keep using host-published ports.
        private IReadOnlyList<string>? _ownNetworks;
        private readonly SemaphoreSlim _ownNetworksLock = new(1, 1);

        private async Task AttachToOwnNetworksAsync(string containerId, CancellationToken ct)
        {
            foreach (var network in await GetOwnNetworksAsync(ct))
            {
                try { await client.Networks.ConnectNetworkAsync(network, new NetworkConnectParameters { Container = containerId }, ct); }
                catch { /* already connected / network gone - non-fatal; the host-port path still works */ }
            }
        }

        private async Task<IReadOnlyList<string>> GetOwnNetworksAsync(CancellationToken ct)
        {
            if (_ownNetworks is not null) return _ownNetworks;
            await _ownNetworksLock.WaitAsync(ct);
            try
            {
                if (_ownNetworks is not null) return _ownNetworks;
                try
                {
                    // CommandBlock's container hostname defaults to its short id; inspecting it finds our
                    // networks. Off the default bridge (no name-based DNS) and the loopback nets.
                    var self = await client.Containers.InspectContainerAsync(System.Net.Dns.GetHostName(), ct);
                    _ownNetworks = self.NetworkSettings?.Networks?.Keys
                        .Where(n => n is not ("bridge" or "host" or "none"))
                        .ToList() ?? [];
                }
                catch { _ownNetworks = []; }   // not in a container (host-run CommandBlock) - use host ports
                return _ownNetworks;
            }
            finally { _ownNetworksLock.Release(); }
        }

        public Task<bool> StartContainerAsync(string id, CancellationToken cancellationToken = default)
        {
            return client.Containers.StartContainerAsync(id, new ContainerStartParameters(), cancellationToken);
        }

        public Task StopContainerAsync(string id, CancellationToken cancellationToken = default)
        {
            return client.Containers.StopContainerAsync(id, new ContainerStopParameters(), cancellationToken);
        }

        public Task RemoveContainerAsync(string id, bool force = false, CancellationToken cancellationToken = default)
        {
            return client.Containers.RemoveContainerAsync(id, new ContainerRemoveParameters { Force = force }, cancellationToken);
        }

        public Task RemoveVolumeAsync(string name, bool force = false, CancellationToken cancellationToken = default)
        {
            return client.Volumes.RemoveAsync(name, force, cancellationToken);
        }

        public async Task<byte[]> ExecCaptureAsync(string containerId, IList<string> command, CancellationToken cancellationToken = default)
        {
            // Cap any single exec at 2 minutes. Without this a stuck pg_dump/mysqldump call would
            // hang the request forever because MultiplexedStream.ReadOutputToEndAsync only returns
            // when the server closes the stream.
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var ct = linkedCts.Token;

            var exec = await client.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
            {
                Cmd = command,
                AttachStdout = true,
                AttachStderr = true,
            }, ct);

            using var stream = await client.Exec.StartAndAttachContainerExecAsync(exec.ID, tty: false, ct);

            // Read directly from the multiplexed stream into MemoryStreams. This avoids
            // ReadOutputToEndAsync's habit of occasionally not returning on subsequent execs
            // against the same container (observed against postgres after a restore+backup pair).
            using var stdout = new MemoryStream();
            using var stderr = new MemoryStream();
            try
            {
                await stream.CopyOutputToAsync(null, stdout, stderr, ct);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException($"docker exec on {containerId} exceeded 2 minutes (command: {string.Join(' ', command)}).");
            }

            var inspect = await client.Exec.InspectContainerExecAsync(exec.ID, ct);
            if (inspect.ExitCode != 0)
            {
                var stderrText = System.Text.Encoding.UTF8.GetString(stderr.ToArray());
                throw new InvalidOperationException($"exec exited with code {inspect.ExitCode}: {stderrText}");
            }
            return stdout.ToArray();
        }

        public async IAsyncEnumerable<string> StreamLogsAsync(string containerId, int tailLines, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var parameters = new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = true,
                Tail = tailLines.ToString(),
                Timestamps = false,
            };

            using var stream = await client.Containers.GetContainerLogsAsync(containerId, tty: false, parameters, cancellationToken);

            var buffer = new byte[16 * 1024];
            while (!cancellationToken.IsCancellationRequested)
            {
                MultiplexedStream.ReadResult read;
                try
                {
                    read = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken);
                }
                catch (OperationCanceledException) { yield break; }
                catch (IOException) { yield break; }

                if (read.EOF) yield break;
                if (read.Count == 0) continue;

                yield return System.Text.Encoding.UTF8.GetString(buffer, 0, read.Count);
            }
        }

        public async Task<Stream> GetArchiveAsync(string containerId, string path, CancellationToken cancellationToken = default)
        {
            var response = await client.Containers.GetArchiveFromContainerAsync(
                containerId,
                new GetArchiveFromContainerParameters { Path = path },
                statOnly: false,
                cancellationToken);
            return response.Stream;
        }

        public Task ExtractArchiveAsync(string containerId, string path, Stream tar, CancellationToken cancellationToken = default)
        {
            return client.Containers.ExtractArchiveToContainerAsync(
                containerId,
                new ContainerPathStatParameters { Path = path, AllowOverwriteDirWithFile = false },
                tar,
                cancellationToken);
        }
    }
}
