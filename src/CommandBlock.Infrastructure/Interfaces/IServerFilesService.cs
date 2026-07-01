namespace CommandBlock.Infrastructure.Interfaces
{
    /// <summary>An entry in a server's data directory. Paths in this API are relative to the
    /// server's /data root.</summary>
    public sealed record FileEntry(string Name, bool IsDirectory, long Size);

    /// <summary>A text file's contents. <see cref="Truncated"/> is set when the file was larger than
    /// the read cap; <see cref="Binary"/> when it doesn't look like UTF-8 text (edit is disabled).</summary>
    public sealed record FileContent(string Content, bool Truncated, bool Binary);

    /// <summary>Browses and edits a server's world/config files inside its container, via Docker's
    /// copy (archive) and exec APIs - so it works for both host-folder and volume storage, and for
    /// servers on remote nodes. All paths are relative to /data and confined to it.</summary>
    public interface IServerFilesService
    {
        Task<IReadOnlyList<FileEntry>> ListAsync(Guid serverId, string path, CancellationToken cancellationToken = default);
        Task<FileContent> ReadTextAsync(Guid serverId, string path, CancellationToken cancellationToken = default);
        Task WriteTextAsync(Guid serverId, string path, string content, CancellationToken cancellationToken = default);
        Task<Stream> OpenReadAsync(Guid serverId, string path, CancellationToken cancellationToken = default);
        Task UploadAsync(Guid serverId, string path, Stream content, CancellationToken cancellationToken = default);
        Task MakeDirAsync(Guid serverId, string path, CancellationToken cancellationToken = default);
        Task DeleteAsync(Guid serverId, string path, CancellationToken cancellationToken = default);
        Task RenameAsync(Guid serverId, string fromPath, string toPath, CancellationToken cancellationToken = default);
    }

    /// <summary>Thrown when a server id doesn't resolve to a container. Mapped to 404 by the API.</summary>
    public sealed class FileServerNotFoundException(Guid id) : Exception($"Server '{id}' has no container.");
}
