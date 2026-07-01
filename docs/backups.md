# World backups

CommandBlock backs a server's world up by archiving its `/data` directory and uploading the tar to an S3-compatible bucket - **SeaweedFS** in the default stack, but any S3 endpoint works.

## How it works

On **Create backup**, CommandBlock:

1. Runs `rcon-cli save-off` and `save-all flush` so the world is flushed and quiescent (best-effort - a stopped or still-booting server backs up anyway).
2. Streams a tar of `/data` out of the container (via the Docker copy API), through a temp file, into the bucket as a multipart upload.
3. Runs `save-on` again.
4. Records the backup (server, filename, size, timestamp).

**Restore** stops the server, extracts the archive back over `/data`, and starts it again. Docker's copy-in works on a stopped container's filesystem.

## Configuration

Backups are off until an S3 target is configured. Set these on the CommandBlock service:

| Setting | Example |
| --- | --- |
| `Backup__Enabled` | `true` |
| `Backup__S3Endpoint` | `http://seaweedfs:8333` |
| `Backup__Bucket` | `commandblock-backups` |
| `Backup__AccessKey` | `commandblock` |
| `Backup__SecretKey` | `…` |
| `Backup__Region` | `us-east-1` (SeaweedFS ignores it) |

The bucket is created automatically on the first upload if it doesn't exist. The bundled `compose.yml` wires a SeaweedFS service and these values for you.

## Using your own S3

Point `Backup__S3Endpoint` at any S3-compatible service (MinIO, AWS S3, Backblaze B2, …) and set the bucket + credentials. Path-style addressing is used, which SeaweedFS and MinIO expect.
