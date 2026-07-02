namespace CommandBlock.Domain
{
    /// <summary>What a backup archive contains.</summary>
    public enum BackupKind
    {
        /// <summary>Just the world folder(s) - restore over the same server's world.</summary>
        World = 0,
        /// <summary>The whole server (/data + config). Can restore in place or spin up a brand-new
        /// server from the dump.</summary>
        Server = 1,
    }

    /// <summary>A backup of a <see cref="ServerInstance"/>: a tar uploaded to the configured
    /// S3/SeaweedFS bucket. The row is metadata; the bytes live in object storage under
    /// <see cref="ObjectKey"/>. <see cref="Kind"/> says whether it's just the world or the full server.</summary>
    public class BackupEntry : BaseEntity
    {
        public required Guid ServerId { get; init; }

        /// <summary>Human-readable name shown in the UI (e.g. "smp-20260701-141530.tar").</summary>
        public required string FileName { get; init; }

        /// <summary>The object key in the bucket the archive is stored under.</summary>
        public required string ObjectKey { get; init; }

        public required long SizeBytes { get; init; }

        /// <summary>World-only or full-server dump.</summary>
        public BackupKind Kind { get; init; } = BackupKind.Server;

        /// <summary>For <see cref="BackupKind.Server"/> backups: JSON of the source server's config
        /// (type/version/memory/java/env), so a new server can be created from the dump. Null for
        /// world backups.</summary>
        public string? Metadata { get; init; }
    }
}
