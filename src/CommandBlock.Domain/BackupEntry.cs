namespace CommandBlock.Domain
{
    /// <summary>A world backup of a <see cref="ServerInstance"/>: a tar of the server's /data
    /// directory uploaded to the configured S3/SeaweedFS bucket. The row is metadata; the bytes
    /// live in object storage under <see cref="ObjectKey"/>.</summary>
    public class BackupEntry : BaseEntity
    {
        public required Guid ServerId { get; init; }

        /// <summary>Human-readable name shown in the UI (e.g. "smp-20260701-141530.tar").</summary>
        public required string FileName { get; init; }

        /// <summary>The object key in the bucket the archive is stored under.</summary>
        public required string ObjectKey { get; init; }

        public required long SizeBytes { get; init; }
    }
}
