using System.Text.RegularExpressions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Command.Server;
using CommandBlock.Application.Dtos.Backup;
using CommandBlock.Application.Mappings.Backup;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.Backup
{
    public sealed class BackupNotFoundException(Guid id) : Exception($"Backup '{id}' was not found.");

    public record CreateBackupCommand(Guid ServerId) : ICommand<BackupEntryDto>;
    public record DeleteBackupCommand(Guid BackupId) : ICommand;
    public record RestoreBackupCommand(Guid BackupId) : ICommand;

    /// <summary>Archives a server's /data directory and uploads it to the backup bucket. Uses RCON
    /// save-off / save-all before the copy (and save-on after) so the world is flushed and quiescent,
    /// avoiding a torn snapshot. RCON steps are best-effort - a stopped/booting server still backs up.</summary>
    public partial class CreateBackupCommandHandler(
        CommandBlockDbContext db,
        IDockerServiceResolver dockerResolver,
        IBackupStorage storage,
        IActivityLogger activity) : ICommandHandler<CreateBackupCommand, BackupEntryDto>
    {
        public async ValueTask<BackupEntryDto> Handle(CreateBackupCommand command, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.FirstOrDefaultAsync(s => s.Id == command.ServerId, cancellationToken)
                ?? throw new ServerNotFoundException(command.ServerId);
            if (!server.IsManaged || server.ContainerId is null)
                throw new InvalidOperationException("This server has no container to back up.");

            var docker = dockerResolver.Resolve(server.NodeId);
            var containerId = server.ContainerId;

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var fileName = $"{Slug(server.DisplayName)}-{stamp}.tar";
            var objectKey = $"{server.Id:N}/{stamp}-{Guid.NewGuid():N}.tar";

            await TryRcon(docker, containerId, ["rcon-cli", "save-off"], cancellationToken);
            await TryRcon(docker, containerId, ["rcon-cli", "save-all", "flush"], cancellationToken);

            long size;
            try
            {
                await using var archive = await docker.GetArchiveAsync(containerId, "/data", cancellationToken);
                size = await storage.UploadAsync(objectKey, archive, cancellationToken);
            }
            finally
            {
                await TryRcon(docker, containerId, ["rcon-cli", "save-on"], CancellationToken.None);
            }

            var entry = new CommandBlock.Domain.BackupEntry
            {
                ServerId = server.Id,
                FileName = fileName,
                ObjectKey = objectKey,
                SizeBytes = size,
            };
            db.BackupEntries.Add(entry);
            await db.SaveChangesAsync(cancellationToken);

            await activity.LogAsync("server.backup", fileName, server.Id, server.ServerType, $"size={size}", cancellationToken);
            return entry.ToDto();
        }

        private static async Task TryRcon(IDockerService docker, string containerId, IList<string> command, CancellationToken ct)
        {
            try { await docker.ExecCaptureAsync(containerId, command, ct); }
            catch { /* RCON may be off or the server still booting - back up regardless */ }
        }

        private static string Slug(string name)
        {
            var s = SlugRegex().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
            return string.IsNullOrEmpty(s) ? "server" : s;
        }

        [GeneratedRegex("[^a-z0-9._-]+")]
        private static partial Regex SlugRegex();
    }

    public class DeleteBackupCommandHandler(
        CommandBlockDbContext db,
        IBackupStorage storage,
        IActivityLogger activity) : ICommandHandler<DeleteBackupCommand>
    {
        public async ValueTask<Unit> Handle(DeleteBackupCommand command, CancellationToken cancellationToken)
        {
            var entry = await db.BackupEntries.FirstOrDefaultAsync(b => b.Id == command.BackupId, cancellationToken)
                ?? throw new BackupNotFoundException(command.BackupId);

            try { await storage.DeleteAsync(entry.ObjectKey, cancellationToken); }
            catch { /* object may already be gone; still drop the row */ }

            db.BackupEntries.Remove(entry);
            await db.SaveChangesAsync(cancellationToken);

            await activity.LogAsync("server.backup.delete", entry.FileName, entry.ServerId, null, null, cancellationToken);
            return Unit.Value;
        }
    }

    /// <summary>Restores a backup by stopping the server, extracting the archive back over /data,
    /// and starting it again. Docker's copy-in works on a stopped container's filesystem.</summary>
    public class RestoreBackupCommandHandler(
        CommandBlockDbContext db,
        IDockerServiceResolver dockerResolver,
        IBackupStorage storage,
        IActivityLogger activity) : ICommandHandler<RestoreBackupCommand>
    {
        public async ValueTask<Unit> Handle(RestoreBackupCommand command, CancellationToken cancellationToken)
        {
            var entry = await db.BackupEntries.FirstOrDefaultAsync(b => b.Id == command.BackupId, cancellationToken)
                ?? throw new BackupNotFoundException(command.BackupId);
            var server = await db.ServerInstances.FirstOrDefaultAsync(s => s.Id == entry.ServerId, cancellationToken)
                ?? throw new ServerNotFoundException(entry.ServerId);
            if (server.ContainerId is null)
                throw new InvalidOperationException("This server has no container to restore into.");

            var docker = dockerResolver.Resolve(server.NodeId);
            var containerId = server.ContainerId;

            await docker.StopContainerAsync(containerId, cancellationToken);
            try
            {
                await using var archive = await storage.OpenReadAsync(entry.ObjectKey, cancellationToken);
                // The tar's top entry is "data/", so extract at "/" to restore /data.
                await docker.ExtractArchiveAsync(containerId, "/", archive, cancellationToken);
            }
            finally
            {
                await docker.StartContainerAsync(containerId, cancellationToken);
            }

            await activity.LogAsync("server.restore", entry.FileName, server.Id, server.ServerType, null, cancellationToken);
            return Unit.Value;
        }
    }
}
