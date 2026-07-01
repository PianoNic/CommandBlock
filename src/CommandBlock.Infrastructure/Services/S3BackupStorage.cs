using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Options;
using CommandBlock.Infrastructure.Interfaces;
using CommandBlock.Infrastructure.Options;

namespace CommandBlock.Infrastructure.Services
{
    /// <summary>Stores world backups in an S3-compatible bucket (SeaweedFS's S3 gateway). Uploads
    /// stream through a temp file so the (potentially large, non-seekable) Docker archive can be
    /// sent as a multipart upload without buffering the whole world in memory.</summary>
    public class S3BackupStorage(IOptions<BackupOptions> options) : IBackupStorage
    {
        private readonly BackupOptions _options = options.Value;

        private (IAmazonS3 client, string bucket) Resolve()
        {
            if (!_options.IsConfigured)
                throw new InvalidOperationException(
                    "Backups are not configured. Set Backup:Enabled and the Backup:S3Endpoint/Bucket/AccessKey/SecretKey values.");

            var config = new AmazonS3Config
            {
                ServiceURL = _options.S3Endpoint,
                ForcePathStyle = true, // SeaweedFS/MinIO use path-style buckets, not virtual-host style.
                AuthenticationRegion = _options.Region,
            };
            var client = new AmazonS3Client(_options.AccessKey, _options.SecretKey, config);
            return (client, _options.Bucket!);
        }

        public async Task<long> UploadAsync(string key, Stream content, CancellationToken cancellationToken = default)
        {
            var (client, bucket) = Resolve();
            using var s3 = client;

            var temp = Path.Combine(Path.GetTempPath(), $"commandblock-backup-{Guid.NewGuid():N}.tar");
            try
            {
                await using (var file = File.Create(temp))
                    await content.CopyToAsync(file, cancellationToken);

                var size = new FileInfo(temp).Length;

                await EnsureBucketAsync(s3, bucket, cancellationToken);
                using var transfer = new TransferUtility(s3);
                await transfer.UploadAsync(temp, bucket, key, cancellationToken);
                return size;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best-effort */ }
            }
        }

        public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        {
            var (client, bucket) = Resolve();
            // The response stream owns the HTTP connection; the caller disposes it. The client is
            // cheap and stateless, so leaking it here is fine (GC collects it with the stream).
            var response = await client.GetObjectAsync(bucket, key, cancellationToken);
            return response.ResponseStream;
        }

        private static async Task EnsureBucketAsync(IAmazonS3 s3, string bucket, CancellationToken ct)
        {
            try { await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, ct); }
            catch (AmazonS3Exception) { /* already exists / owned by us - fine */ }
        }

        public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            var (client, bucket) = Resolve();
            using var s3 = client;
            await s3.DeleteObjectAsync(bucket, key, cancellationToken);
        }
    }
}
