using System.Formats.Tar;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Command.Server;
using CommandBlock.Application.Dtos.Backup;
using CommandBlock.Application.Mappings.Backup;
using CommandBlock.Domain;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.Backup
{
    public sealed class BackupNotFoundException(Guid id) : Exception($"Backup '{id}' was not found.");

    /// <summary>The source server's config, captured with a Server backup so a new server can be
    /// created from the dump.</summary>
    public sealed record BackupServerConfig(
        string ServerType, string? Version, string? ModpackRef, string Memory,
        string? JavaVersion, bool UseAikarFlags, string? JvmArgs, string? ExtraEnv);

    public record CreateBackupCommand(Guid ServerId, BackupKind Kind = BackupKind.Server) : ICommand<BackupEntryDto>;
    public record DeleteBackupCommand(Guid BackupId) : ICommand;
    public record RestoreBackupCommand(Guid BackupId) : ICommand;

    /// <summary>Archives a server and uploads it to the backup bucket. A <see cref="BackupKind.World"/>
    /// backup grabs just the world folder; a <see cref="BackupKind.Server"/> backup grabs the whole
    /// /data directory plus the server's config (so it can seed a brand-new server). RCON save-off /
    /// save-all runs first (save-on after) so the world is flushed - best-effort, a stopped server
    /// still backs up.</summary>
    public partial class CreateBackupCommandHandler(
        CommandBlockDbContext db,
        IDockerService docker,
        IServerFilesService files,
        IBackupStorage storage,
        IActivityLogger activity) : ICommandHandler<CreateBackupCommand, BackupEntryDto>
    {
        public async ValueTask<BackupEntryDto> Handle(CreateBackupCommand command, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.FirstOrDefaultAsync(s => s.Id == command.ServerId, cancellationToken)
                ?? throw new ServerNotFoundException(command.ServerId);
            if (!server.IsManaged || server.ContainerId is null)
                throw new InvalidOperationException("This server has no container to back up.");

            var containerId = server.ContainerId;

            // What to archive: the world folder (plus its dimension siblings) for a World backup, or the
            // whole /data for a Server backup.
            string? metadata = null;
            var level = "world";
            if (command.Kind == BackupKind.World)
            {
                level = await ReadLevelNameAsync(command.ServerId, cancellationToken);
            }
            else
            {
                metadata = JsonSerializer.Serialize(new BackupServerConfig(
                    server.ServerType, server.Version, server.ModpackRef, server.Memory,
                    server.JavaVersion, server.UseAikarFlags, server.JvmArgs, server.ExtraEnv));
            }

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var tag = command.Kind == BackupKind.World ? "world" : "server";
            var fileName = $"{Slug(server.DisplayName)}-{tag}-{stamp}.tar";
            var objectKey = $"{server.Id:N}/{stamp}-{Guid.NewGuid():N}.tar";

            await TryRcon(docker, containerId, ["rcon-cli", "save-off"], cancellationToken);
            await TryRcon(docker, containerId, ["rcon-cli", "save-all", "flush"], cancellationToken);

            long size;
            try
            {
                await using var archive = command.Kind == BackupKind.World
                    ? await ArchiveWorldAsync(containerId, level, cancellationToken)
                    : await docker.GetArchiveAsync(containerId, "/data", cancellationToken);
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
                Kind = command.Kind,
                Metadata = metadata,
            };
            db.BackupEntries.Add(entry);
            await db.SaveChangesAsync(cancellationToken);

            await activity.LogAsync($"server.backup.{tag}", fileName, server.Id, server.ServerType, $"size={size}", cancellationToken);
            return entry.ToDto();
        }

        /// <summary>Reads level-name from server.properties (default "world"). Paper-style servers keep
        /// the nether/end in sibling folders (level_nether / level_the_end); <see cref="ArchiveWorldAsync"/>
        /// bundles those alongside the overworld.</summary>
        private async Task<string> ReadLevelNameAsync(Guid serverId, CancellationToken ct)
        {
            try
            {
                var file = await files.ReadTextAsync(serverId, "server.properties", ct);
                var m = LevelNameRegex().Match(file.Content);
                var name = m.Success ? m.Groups[1].Value.Trim() : "";
                return string.IsNullOrEmpty(name) ? "world" : name;
            }
            catch { return "world"; }
        }

        /// <summary>Builds a single tar of the world plus its dimension siblings. Paper/Purpur/Spigot split
        /// the nether and end into <c>{level}_nether</c> / <c>{level}_the_end</c> folders next to the
        /// overworld (vanilla nests them, so those are already inside the overworld tar). Each folder is
        /// copied out of the container as its own tar and their entries are streamed into one combined
        /// archive through a pipe, so nothing is buffered whole in memory. Missing siblings are skipped.</summary>
        private async Task<Stream> ArchiveWorldAsync(string containerId, string level, CancellationToken ct)
        {
            var folders = new[] { level, $"{level}_nether", $"{level}_the_end" };
            var pipe = new Pipe();

            _ = Task.Run(async () =>
            {
                try
                {
                    var outStream = pipe.Writer.AsStream(leaveOpen: true);
                    await using (var tarWriter = new TarWriter(outStream, TarEntryFormat.Pax, leaveOpen: true))
                    {
                        foreach (var folder in folders)
                        {
                            Stream src;
                            try { src = await docker.GetArchiveAsync(containerId, $"/data/{folder}", ct); }
                            catch { continue; } // dimension folder absent (vanilla, or no nether/end yet) - skip
                            await using (src)
                            {
                                using var reader = new TarReader(src, leaveOpen: true);
                                while (await reader.GetNextEntryAsync(cancellationToken: ct) is { } entry)
                                    await tarWriter.WriteEntryAsync(entry, ct);
                            }
                        }
                    }
                    await pipe.Writer.CompleteAsync();
                }
                catch (Exception ex)
                {
                    await pipe.Writer.CompleteAsync(ex);
                }
            }, ct);

            return pipe.Reader.AsStream();
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

        [GeneratedRegex(@"^level-name=(.*)$", RegexOptions.Multiline)]
        private static partial Regex LevelNameRegex();

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

    /// <summary>Restores a backup by stopping the server, extracting the archive back, and starting it
    /// again. A Server backup extracts over the whole /data (tar top "data/", so at "/"); a World
    /// backup extracts just the world folder back into /data.</summary>
    public class RestoreBackupCommandHandler(
        CommandBlockDbContext db,
        IDockerService docker,
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

            var containerId = server.ContainerId;
            var extractPath = entry.Kind == BackupKind.World ? "/data" : "/";

            await docker.StopContainerAsync(containerId, cancellationToken);
            try
            {
                await using var archive = await storage.OpenReadAsync(entry.ObjectKey, cancellationToken);
                await docker.ExtractArchiveAsync(containerId, extractPath, archive, cancellationToken);
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
