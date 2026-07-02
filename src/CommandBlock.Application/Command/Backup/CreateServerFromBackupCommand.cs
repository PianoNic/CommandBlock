using System.Text.Json;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using CommandBlock.Application.Command.Server;
using CommandBlock.Application.Dtos.Server;
using CommandBlock.Application.Mappings.Server;
using CommandBlock.Application.Options;
using CommandBlock.Domain;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.Backup
{
    /// <summary>Spins up a brand-new server from a Server backup: recreates the source server's config
    /// (type/version/memory/java/env) under a new name + hostname, provisions its container, seeds
    /// /data from the dump, and starts it.</summary>
    public record CreateServerFromBackupCommand(Guid BackupId, string DisplayName, string Hostname) : ICommand<ServerInstanceDto>;

    public class CreateServerFromBackupCommandHandler(
        CommandBlockDbContext db,
        IDockerServiceResolver dockerResolver,
        IBackupStorage storage,
        IOptions<CommandBlockOptions> options,
        IActivityLogger activity) : ICommandHandler<CreateServerFromBackupCommand, ServerInstanceDto>
    {
        private readonly CommandBlockOptions _options = options.Value;

        public async ValueTask<ServerInstanceDto> Handle(CreateServerFromBackupCommand command, CancellationToken cancellationToken)
        {
            var backup = await db.BackupEntries.AsNoTracking().FirstOrDefaultAsync(b => b.Id == command.BackupId, cancellationToken)
                ?? throw new BackupNotFoundException(command.BackupId);
            if (backup.Kind != BackupKind.Server || string.IsNullOrEmpty(backup.Metadata))
                throw new InvalidOperationException("Only a full server backup can seed a new server.");

            var config = JsonSerializer.Deserialize<BackupServerConfig>(backup.Metadata)
                ?? throw new InvalidOperationException("This backup's server config could not be read.");

            var hostname = command.Hostname.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(hostname))
                throw new ArgumentException("Hostname is required.", nameof(command));
            if (await db.ServerInstances.AnyAsync(s => s.Hostname == hostname, cancellationToken))
                throw new InvalidOperationException($"A server with hostname '{hostname}' already exists.");

            var docker = dockerResolver.Resolve(null);
            var instanceId = Guid.NewGuid();
            var containerName = $"commandblock-mc-{instanceId.ToString("N")[..8]}";
            var bindSpec = _options.Storage.ResolveBindForContainer(containerName, "/data");

            var instance = new ServerInstance
            {
                Id = instanceId,
                ServerType = config.ServerType,
                Version = config.Version,
                ModpackRef = config.ModpackRef,
                Memory = config.Memory,
                JavaVersion = config.JavaVersion,
                UseAikarFlags = config.UseAikarFlags,
                JvmArgs = config.JvmArgs,
                ExtraEnv = config.ExtraEnv,
                DisplayName = string.IsNullOrWhiteSpace(command.DisplayName) ? "Restored server" : command.DisplayName.Trim(),
                Hostname = hostname,
                Port = ServerContainerSpec.McPort,
                ContainerName = containerName,
            };

            var createParams = ServerContainerSpec.BuildCreateParams(instance, containerName, bindSpec);
            await docker.PullImageAsync(ServerContainerSpec.Image, ServerContainerSpec.ImageTag(instance), cancellationToken);
            var createResult = await docker.CreateContainerAsync(createParams, cancellationToken);

            var startedOk = false;
            try
            {
                // Seed /data from the dump before first boot (tar top entry is "data/", so extract at "/").
                await using (var archive = await storage.OpenReadAsync(backup.ObjectKey, cancellationToken))
                    await docker.ExtractArchiveAsync(createResult.ID, "/", archive, cancellationToken);

                await docker.StartContainerAsync(createResult.ID, cancellationToken);

                instance.ContainerId = createResult.ID;
                db.ServerInstances.Add(instance);
                await db.SaveChangesAsync(cancellationToken);

                await activity.LogAsync("server.create.from-backup", containerName, instance.Id, instance.ServerType,
                    $"host={hostname}, from={backup.FileName}", cancellationToken);

                startedOk = true;
                return instance.ToDto(state: "created");
            }
            finally
            {
                if (!startedOk)
                {
                    try { await docker.RemoveContainerAsync(createResult.ID, force: true, CancellationToken.None); } catch { }
                }
            }
        }
    }
}
