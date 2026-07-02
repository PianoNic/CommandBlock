using Docker.DotNet.Models;

namespace CommandBlock.Infrastructure.Interfaces
{
    public interface IDockerService
    {
        Task<bool> PingAsync(CancellationToken cancellationToken = default);

        /// <summary>The Docker engine version string (e.g. "27.3.1") of the daemon this client talks to.</summary>
        Task<string> GetVersionAsync(CancellationToken cancellationToken = default);

        Task<IList<ContainerListResponse>> ListContainersAsync(bool all = true, CancellationToken cancellationToken = default);

        Task<ContainerInspectResponse> InspectContainerAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>Current memory usage in bytes of a running container, from a one-shot stats
        /// sample (cache excluded, like `docker stats`). Null when stopped or stats can't be read.</summary>
        Task<long?> GetContainerMemoryBytesAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>Total physical memory of the Docker host in bytes (from the daemon's info), or 0
        /// if it can't be read. Used to bound how much a new server may be allocated.</summary>
        Task<long> GetHostMemoryTotalBytesAsync(CancellationToken cancellationToken = default);

        Task PullImageAsync(string image, string tag = "latest", CancellationToken cancellationToken = default);

        Task<CreateContainerResponse> CreateContainerAsync(CreateContainerParameters parameters, CancellationToken cancellationToken = default);

        Task<bool> StartContainerAsync(string id, CancellationToken cancellationToken = default);

        Task StopContainerAsync(string id, CancellationToken cancellationToken = default);

        Task RemoveContainerAsync(string id, bool force = false, CancellationToken cancellationToken = default);

        Task RemoveVolumeAsync(string name, bool force = false, CancellationToken cancellationToken = default);

        /// <summary>Runs a command inside a running container and returns its stdout as raw bytes.</summary>
        Task<byte[]> ExecCaptureAsync(string containerId, IList<string> command, CancellationToken cancellationToken = default);

        /// <summary>Follows a container's combined stdout/stderr, yielding decoded text chunks until
        /// the stream ends or is cancelled. Used to stream the live server console.</summary>
        IAsyncEnumerable<string> StreamLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken = default);

        /// <summary>Streams a tar archive of <paramref name="path"/> from a container (Docker "copy
        /// out"). The tar's top-level entry is the basename of the path (e.g. "/data" -&gt; "data/…").</summary>
        Task<Stream> GetArchiveAsync(string containerId, string path, CancellationToken cancellationToken = default);

        /// <summary>Extracts a tar archive into <paramref name="path"/> inside a container (Docker
        /// "copy in"). Pass the parent dir (e.g. "/") of what the tar contains.</summary>
        Task ExtractArchiveAsync(string containerId, string path, Stream tar, CancellationToken cancellationToken = default);
    }
}
