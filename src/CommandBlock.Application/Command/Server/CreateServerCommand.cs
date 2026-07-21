using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using CommandBlock.Application.Dtos.Server;
using CommandBlock.Application.Mappings.Server;
using CommandBlock.Application.Options;
using CommandBlock.Domain;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.Server
{
    /// <summary>Creates a Minecraft server: provisions an <c>itzg/minecraft-server</c> container for
    /// the chosen loader/modpack, records the <see cref="ServerInstance"/>, and starts it. The
    /// container publishes no host port - it sits on CommandBlock's Docker network (containers are
    /// auto-attached at create time) and is reached only through the router by <see cref="ServerInstance.Hostname"/>.</summary>
    public record CreateServerCommand(
        string ServerType,
        string DisplayName,
        string? Hostname,
        string Memory,
        string? Version = null,
        string? ModpackRef = null,
        string? JavaVersion = null,
        bool UseAikarFlags = false,
        bool AllowAnyClientVersion = false,
        string? JvmArgs = null,
        string? ExtraEnv = null,
        bool RoutedThroughProxy = true,
        int? LanPort = null,
        string? LanBindAddress = null) : ICommand<ServerInstanceDto>;

    public class CreateServerCommandHandler(
        IDockerService docker,
        CommandBlockDbContext db,
        IOptions<CommandBlockOptions> options,
        IActivityLogger activity) : ICommandHandler<CreateServerCommand, ServerInstanceDto>
    {
        private readonly CommandBlockOptions _options = options.Value;

        public async ValueTask<ServerInstanceDto> Handle(CreateServerCommand command, CancellationToken cancellationToken)
        {
            var serverType = NormalizeType(command.ServerType);

            // A server is reached either through the router by hostname or directly on a port of its own,
            // so only the chosen half is required - asking for a hostname on a LAN server that will never
            // use one is friction with nothing behind it.
            string? hostname = null;
            int? lanPort = null;
            string? lanBind = null;

            if (command.RoutedThroughProxy)
            {
                hostname = command.Hostname?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(hostname))
                    throw new ArgumentException("Hostname is required.", nameof(command));

                // Reject a duplicate hostname up front - a clean domain error beats a DbUpdateException
                // bubbling out to the API.
                if (await db.ServerInstances.AnyAsync(s => s.Hostname == hostname, cancellationToken))
                    throw new InvalidOperationException($"A server with hostname '{hostname}' already exists.");
            }
            else
            {
                lanPort = command.LanPort;
                if (lanPort is null or < 1 or > 65535)
                    throw new ArgumentException("A port between 1 and 65535 is required to reach this server directly.", nameof(command));

                lanBind = string.IsNullOrWhiteSpace(command.LanBindAddress) ? null : command.LanBindAddress.Trim();
                if (lanBind is not null && !System.Net.IPAddress.TryParse(lanBind, out _))
                    throw new ArgumentException($"'{lanBind}' isn't a valid IP address.", nameof(command));

                if (await db.ServerInstances.AnyAsync(s => s.LanPort == lanPort, cancellationToken))
                    throw new InvalidOperationException($"Port {lanPort} is already published by another server.");
            }

            var instanceId = Guid.NewGuid();
            var instanceIdShort = instanceId.ToString("N")[..8];
            var containerName = $"commandblock-mc-{instanceIdShort}";
            var bindSpec = _options.Storage.ResolveBindForContainer(containerName, "/data");

            var instance = new ServerInstance
            {
                Id = instanceId,
                ServerType = serverType,
                Version = command.Version,
                ModpackRef = command.ModpackRef,
                Memory = command.Memory,
                JavaVersion = string.IsNullOrWhiteSpace(command.JavaVersion) ? null : command.JavaVersion,
                UseAikarFlags = command.UseAikarFlags,
                AllowAnyClientVersion = command.AllowAnyClientVersion,
                JvmArgs = string.IsNullOrWhiteSpace(command.JvmArgs) ? null : command.JvmArgs,
                ExtraEnv = string.IsNullOrWhiteSpace(command.ExtraEnv) ? null : command.ExtraEnv,
                DisplayName = command.DisplayName,
                Hostname = hostname,
                RoutedThroughProxy = command.RoutedThroughProxy,
                LanPort = lanPort,
                LanBindAddress = lanBind,
                IsPublic = lanPort is not null,
                Port = ServerContainerSpec.McPort,
                ContainerName = containerName,
            };

            var createParams = ServerContainerSpec.BuildCreateParams(instance, containerName, bindSpec);
            await docker.PullImageAsync(ServerContainerSpec.Image, ServerContainerSpec.ImageTag(instance), cancellationToken);

            var createResult = await docker.CreateContainerAsync(createParams, cancellationToken);

            // From here on, any failure means we own a container the caller never sees. Track
            // success and tear it down otherwise, so a retry isn't blocked by a stale container
            // holding the name/hostname.
            var startedOk = false;
            try
            {
                await docker.StartContainerAsync(createResult.ID, cancellationToken);

                instance.ContainerId = createResult.ID;
                db.ServerInstances.Add(instance);
                await db.SaveChangesAsync(cancellationToken);

                await activity.LogAsync("server.create", containerName, instance.Id, serverType,
                    $"host={hostname}, version={command.Version ?? command.ModpackRef ?? "latest"}", cancellationToken);

                startedOk = true;
                // The server boots asynchronously (modpacks can take minutes); live state is
                // resolved from Docker on subsequent reads. Report "created" for the immediate result.
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

        private static string NormalizeType(string serverType)
        {
            if (string.IsNullOrWhiteSpace(serverType))
                throw new ArgumentException("ServerType is required.", nameof(serverType));
            return serverType.Trim().ToUpperInvariant();
        }
    }
}
