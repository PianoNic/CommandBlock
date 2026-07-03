using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using CommandBlock.Application.Dtos.Server;
using CommandBlock.Application.Mappings.Server;
using CommandBlock.Application.Options;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.Server
{
    /// <summary>Applies runtime settings (memory, Java version, JVM/Aikar flags, extra env) to an
    /// existing server. Docker bakes env and image in at container-create time, so these can't be
    /// changed on a running container - we recreate it in place: remove the old container, create a
    /// new one under the same name with the new spec, and start it. The world lives in the data
    /// volume/bind (keyed by container name), so it survives untouched.</summary>
    public record RecreateServerCommand(
        Guid Id,
        string Memory,
        string? JavaVersion = null,
        bool UseAikarFlags = false,
        string? JvmArgs = null,
        string? ExtraEnv = null) : ICommand<ServerInstanceDto>;

    public class RecreateServerCommandHandler(
        IDockerService docker,
        CommandBlockDbContext db,
        IOptions<CommandBlockOptions> options,
        IActivityLogger activity) : ICommandHandler<RecreateServerCommand, ServerInstanceDto>
    {
        private readonly CommandBlockOptions _options = options.Value;

        public async ValueTask<ServerInstanceDto> Handle(RecreateServerCommand command, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
                ?? throw new ServerNotFoundException(command.Id);

            if (!server.IsManaged || server.ContainerName is null)
                throw new InvalidOperationException("This server isn't managed by CommandBlock - its runtime can't be changed.");

            if (string.IsNullOrWhiteSpace(command.Memory))
                throw new ArgumentException("Memory is required (e.g. \"4G\").");

            // Apply the new settings to the entity, then rebuild the container spec from it.
            server.Memory = command.Memory.Trim();
            server.JavaVersion = string.IsNullOrWhiteSpace(command.JavaVersion) ? null : command.JavaVersion.Trim();
            server.UseAikarFlags = command.UseAikarFlags;
            server.JvmArgs = string.IsNullOrWhiteSpace(command.JvmArgs) ? null : command.JvmArgs;
            server.ExtraEnv = string.IsNullOrWhiteSpace(command.ExtraEnv) ? null : command.ExtraEnv;

            var containerName = server.ContainerName;
            var bindSpec = _options.Storage.ResolveBindForContainer(containerName, "/data");
            var createParams = ServerContainerSpec.BuildCreateParams(server, containerName, bindSpec);

            await docker.PullImageAsync(ServerContainerSpec.Image, ServerContainerSpec.ImageTag(server), cancellationToken);

            // Remove the old container (the data volume/bind is keyed by name and stays). Then
            // recreate under the same name so labels/routing/data all line up.
            if (server.ContainerId is not null)
            {
                try { await docker.RemoveContainerAsync(server.ContainerId, force: true, cancellationToken); }
                catch { /* already gone - proceed to recreate */ }
            }

            var createResult = await docker.CreateContainerAsync(createParams, cancellationToken);
            await docker.StartContainerAsync(createResult.ID, cancellationToken);

            server.ContainerId = createResult.ID;
            await db.SaveChangesAsync(cancellationToken);

            await activity.LogAsync("server.recreate", containerName, server.Id, server.ServerType,
                $"memory={server.Memory}, java={server.JavaVersion ?? "auto"}, aikar={server.UseAikarFlags}", cancellationToken);

            return server.ToDto(state: "created");
        }
    }
}
