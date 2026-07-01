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
