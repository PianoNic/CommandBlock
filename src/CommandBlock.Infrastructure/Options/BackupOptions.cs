namespace CommandBlock.Infrastructure.Options
{
    /// <summary>S3/SeaweedFS target for world backups. Bound from the "Backup" config section
    /// (e.g. Backup__S3Endpoint, Backup__Bucket, Backup__AccessKey, Backup__SecretKey).</summary>
    public sealed class BackupOptions
    {
        public bool Enabled { get; set; }

        /// <summary>The S3 endpoint of the SeaweedFS S3 gateway, e.g. "http://seaweedfs:8333".</summary>
        public string? S3Endpoint { get; set; }

        public string? Bucket { get; set; }
        public string? AccessKey { get; set; }
        public string? SecretKey { get; set; }

        /// <summary>Region label. SeaweedFS ignores it, but the AWS SDK requires one.</summary>
        public string Region { get; set; } = "us-east-1";

        public bool IsConfigured =>
            Enabled
            && !string.IsNullOrWhiteSpace(S3Endpoint)
            && !string.IsNullOrWhiteSpace(Bucket)
            && !string.IsNullOrWhiteSpace(AccessKey)
            && !string.IsNullOrWhiteSpace(SecretKey);
    }
}
