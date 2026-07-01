namespace CommandBlock.Infrastructure.Interfaces
{
    /// <summary>Object storage for world-backup archives (backed by S3/SeaweedFS).</summary>
    public interface IBackupStorage
    {
        /// <summary>Uploads a stream under <paramref name="key"/> and returns the stored byte count.</summary>
        Task<long> UploadAsync(string key, Stream content, CancellationToken cancellationToken = default);

        /// <summary>Opens the object for reading. Caller disposes the returned stream.</summary>
        Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);

        Task DeleteAsync(string key, CancellationToken cancellationToken = default);
    }
}
