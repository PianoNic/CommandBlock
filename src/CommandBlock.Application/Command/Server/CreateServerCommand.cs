using Docker.DotNet.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using CommandBlock.Application.Containers;
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
        string Hostname,
        string Memory,
        string? Version = null,
        string? ModpackRef = null,
        Guid? NodeId = null) : ICommand<ServerInstanceDto>;

    public class CreateServerCommandHandler(
        IDockerServiceResolver dockerResolver,
        CommandBlockDbContext db,
        IOptions<CommandBlockOptions> options,
        IActivityLogger activity) : ICommandHandler<CreateServerCommand, ServerInstanceDto>
    {
        private const string Image = "itzg/minecraft-server";
        private const string ImageTag = "latest";
        private const int McPort = 25565;

        private readonly CommandBlockOptions _options = options.Value;

        public async ValueTask<ServerInstanceDto> Handle(CreateServerCommand command, CancellationToken cancellationToken)
        {
            var docker = dockerResolver.Resolve(command.NodeId);

            var serverType = NormalizeType(command.ServerType);
            var hostname = command.Hostname.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(hostname))
                throw new ArgumentException("Hostname is required.", nameof(command));

            // Reject a duplicate hostname up front. The DB has a unique index too, but a clean
            // domain error beats a DbUpdateException bubbling out to the API.
            if (await db.ServerInstances.AnyAsync(s => s.Hostname == hostname, cancellationToken))
                throw new InvalidOperationException($"A server with hostname '{hostname}' already exists.");

            var instanceId = Guid.NewGuid();
            var instanceIdShort = instanceId.ToString("N")[..8];
            var containerName = $"commandblock-mc-{instanceIdShort}";
            var bindSpec = _options.Storage.ResolveBindForContainer(containerName, "/data");

            await docker.PullImageAsync(Image, ImageTag, cancellationToken);

            var env = BuildEnv(serverType, command.Version, command.ModpackRef, command.Memory);

            var createParams = new CreateContainerParameters
            {
                Image = $"{Image}:{ImageTag}",
                Name = containerName,
                Env = env,
                // Expose the MC port so the router (on the same Docker network) can dial it by
                // container name. Deliberately no PortBindings: managed servers stay internal and
                // are reached only through the router, so the host exposes a single public port.
                ExposedPorts = new Dictionary<string, EmptyStruct>
                {
                    [$"{McPort}/tcp"] = default,
                },
                HostConfig = new HostConfig
                {
                    Binds = new List<string> { bindSpec },
                    RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped },
                },
                Labels = CommandBlockContainerLabels.ForServer(serverType, instanceId, hostname, command.DisplayName),
            };

            var createResult = await docker.CreateContainerAsync(createParams, cancellationToken);

            // From here on, any failure means we own a container the caller never sees. Track
            // success and tear it down otherwise, so a retry isn't blocked by a stale container
            // holding the name/hostname.
            var startedOk = false;
            try
            {
                await docker.StartContainerAsync(createResult.ID, cancellationToken);

                var instance = new ServerInstance
                {
                    Id = instanceId,
                    ServerType = serverType,
                    Version = command.Version,
                    ModpackRef = command.ModpackRef,
                    Memory = command.Memory,
                    DisplayName = command.DisplayName,
                    Hostname = hostname,
                    Port = McPort,
                    ContainerName = containerName,
                    ContainerId = createResult.ID,
                    NodeId = command.NodeId,
                };
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

        /// <summary>Builds the <c>itzg/minecraft-server</c> environment for the chosen loader or
        /// modpack. Modpack installers derive their own Minecraft version from the pack, so VERSION
        /// is only sent for plain loaders; each installer reads a different env var for the pack ref.</summary>
        internal static List<string> BuildEnv(string serverType, string? version, string? modpackRef, string memory)
        {
            if (string.IsNullOrWhiteSpace(memory))
                throw new ArgumentException("Memory is required (e.g. \"4G\").", nameof(memory));

            var env = new List<string>
            {
                "EULA=TRUE",
                $"TYPE={serverType}",
                $"MEMORY={memory}",
            };

            switch (serverType)
            {
                case "MODRINTH":
                    RequireModpack(serverType, modpackRef);
                    env.Add($"MODRINTH_MODPACK={modpackRef}");
                    break;
                case "CURSEFORGE":
                case "AUTO_CURSEFORGE":
                    RequireModpack(serverType, modpackRef);
                    // Requires CF_API_KEY on the CommandBlock container to download from CurseForge.
                    env.Add($"CF_SLUG={modpackRef}");
                    break;
                case "FTBA":
                    RequireModpack(serverType, modpackRef);
                    env.Add($"FTB_MODPACK_ID={modpackRef}");
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(version))
                        env.Add($"VERSION={version}");
                    break;
            }

            return env;
        }

        private static void RequireModpack(string serverType, string? modpackRef)
        {
            if (string.IsNullOrWhiteSpace(modpackRef))
                throw new ArgumentException($"ServerType '{serverType}' requires a modpack reference (ModpackRef).");
        }
    }
}
