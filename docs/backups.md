# Backups

CommandBlock backs a server up by archiving it out of its container and uploading the tar to an S3-compatible bucket - **SeaweedFS** in the default stack, but any S3 endpoint works.

## World vs. server backups

Two kinds, picked when you click **Create backup**:

| Kind | Contents | Use it to |
| --- | --- | --- |
| **World** | The world folder and its nether/end dimensions. | Snapshot progress before a risky change - small and quick. |
| **Server** | The whole `/data` directory plus the server's config (type, version, memory, Java, env). | Fully restore a server, or seed a brand-new one from the dump. |

## How it works

On **Create backup**, CommandBlock runs `rcon-cli save-off` and `save-all flush` so the world is flushed and quiescent (best-effort - a stopped or still-booting server backs up anyway), streams a tar out of the container into the bucket, then runs `save-on` again and records the backup (server, kind, filename, size, timestamp).

## Restore

**Restore** stops the server, extracts the archive back into place, and starts it again:

- a **World** backup restores the world folder (and its dimensions) into `/data`;
- a **Server** backup restores the whole `/data`.

## Create a server from a backup

A **Server** backup carries the source server's config, so you can seed a brand-new server from it. On a server backup choose **New server from backup**, give it a display name and a unique hostname, and CommandBlock provisions a fresh container from the saved config and seeds it with the backed-up data - handy for cloning a setup or moving one to a new hostname.

## Scheduled backups

Each server can back itself up on a schedule. Pick a preset (hourly / daily / weekly) or enter a **custom cron** expression; CommandBlock runs the backup in the background and records it like any other.

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
