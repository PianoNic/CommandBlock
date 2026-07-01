using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using CommandBlock.Application.Options;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.Server
{
    /// <summary>Thrown when a server id doesn't resolve. The API maps it to 404.</summary>
    public sealed class ServerNotFoundException(Guid id)
        : Exception($"Server '{id}' was not found.");

    public record StartServerCommand(Guid Id) : ICommand;
    public record StopServerCommand(Guid Id) : ICommand;
    public record DeleteServerCommand(Guid Id) : ICommand;

    public class StartServerCommandHandler(
        CommandBlockDbContext db,
        IDockerServiceResolver dockerResolver,
        IActivityLogger activity) : ICommandHandler<StartServerCommand>
    {
        public async ValueTask<Unit> Handle(StartServerCommand command, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
                ?? throw new ServerNotFoundException(command.Id);
            if (server.ContainerId is null)
                throw new InvalidOperationException("This server has no container - nothing to start.");

            await dockerResolver.Resolve(server.NodeId).StartContainerAsync(server.ContainerId, cancellationToken);
            await activity.LogAsync("server.start", server.ContainerName ?? server.DisplayName, server.Id, server.ServerType, null, cancellationToken);
            return Unit.Value;
        }
    }

    public class StopServerCommandHandler(
        CommandBlockDbContext db,
        IDockerServiceResolver dockerResolver,
        IActivityLogger activity) : ICommandHandler<StopServerCommand>
    {
        public async ValueTask<Unit> Handle(StopServerCommand command, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
                ?? throw new ServerNotFoundException(command.Id);
            if (server.ContainerId is null)
                throw new InvalidOperationException("This server has no container - nothing to stop.");

            await dockerResolver.Resolve(server.NodeId).StopContainerAsync(server.ContainerId, cancellationToken);
            await activity.LogAsync("server.stop", server.ContainerName ?? server.DisplayName, server.Id, server.ServerType, null, cancellationToken);
            return Unit.Value;
        }
    }

    public class DeleteServerCommandHandler(
        CommandBlockDbContext db,
        IDockerServiceResolver dockerResolver,
        IActivityLogger activity,
        IOptions<CommandBlockOptions> options) : ICommandHandler<DeleteServerCommand>
    {
        public async ValueTask<Unit> Handle(DeleteServerCommand command, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
                ?? throw new ServerNotFoundException(command.Id);

            if (server.IsManaged && server.ContainerName is not null && server.ContainerId is not null)
            {
                var docker = dockerResolver.Resolve(server.NodeId);

                try { await docker.RemoveContainerAsync(server.ContainerId, force: true, cancellationToken); }
                catch { /* container may already be gone */ }

                // World data lives in the "{containerName}-data" volume (StorageOptions default).
                try { await docker.RemoveVolumeAsync($"{server.ContainerName}-data", force: true, cancellationToken); }
                catch { /* volume may already be gone */ }

                // Host-folder storage cleanup only applies to local servers - the folder lives on
                // the node's filesystem otherwise, which this process can't see.
                var hostFolder = options.Value.Storage.TryResolveHostFolderForContainer(server.ContainerName);
                if (hostFolder is not null && Directory.Exists(hostFolder))
                {
                    try { Directory.Delete(hostFolder, recursive: true); }
                    catch { /* not accessible from this process; user can clean up manually */ }
                }
            }

            db.ServerInstances.Remove(server);
            await db.SaveChangesAsync(cancellationToken);
            await activity.LogAsync("server.delete", server.ContainerName ?? server.DisplayName, server.Id, server.ServerType, null, cancellationToken);
            return Unit.Value;
        }
    }
}
