using System.Net;
using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure;

namespace CommandBlock.Application.Command.Server
{
    /// <summary>Sets how a server is reachable: through the router by hostname, or directly on a published
    /// host port - one or the other, never both. Publishing is baked into the container at create time, so
    /// switching mode (or changing the port/bind address) recreates the container; the world is bind-mounted
    /// by name and survives. Switching back to the router is just a DB write, since the router resolves per
    /// connection.</summary>
    /// <param name="Hostname">Only used when moving onto the router, for a server created without one.</param>
    public record UpdateNetworkCommand(Guid ServerId, bool RoutedThroughProxy, int? LanPort, string? LanBindAddress, string? Hostname = null) : ICommand;

    public class UpdateNetworkCommandHandler(CommandBlockDbContext db, IMediator mediator) : ICommandHandler<UpdateNetworkCommand>
    {
        public async ValueTask<Unit> Handle(UpdateNetworkCommand command, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.FirstOrDefaultAsync(s => s.Id == command.ServerId, cancellationToken)
                ?? throw new ServerNotFoundException(command.ServerId);

            // The two modes are exclusive, so the flag decides and the other half is cleared rather than
            // quietly kept - a stale port on a routed server would republish itself on the next recreate.
            var port = command.RoutedThroughProxy ? null : command.LanPort;
            var bind = command.RoutedThroughProxy || string.IsNullOrWhiteSpace(command.LanBindAddress)
                ? null
                : command.LanBindAddress.Trim();

            if (command.RoutedThroughProxy)
            {
                var hostname = string.IsNullOrWhiteSpace(command.Hostname)
                    ? server.Hostname
                    : command.Hostname.Trim().ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(hostname))
                    throw new ArgumentException("A hostname is required to reach this server through the router.");

                if (await db.ServerInstances.AnyAsync(s => s.Hostname == hostname && s.Id != server.Id, cancellationToken))
                    throw new ArgumentException($"A server with hostname '{hostname}' already exists.");

                server.Hostname = hostname;
            }
            else
            {
                if (port is null)
                    throw new ArgumentException("A port is required to reach this server directly.");
            }

            if (port is not null)
            {
                if (port is < 1 or > 65535)
                    throw new ArgumentException("Port must be between 1 and 65535.");

                if (bind is not null && !IPAddress.TryParse(bind, out _))
                    throw new ArgumentException($"'{bind}' isn't a valid IP address. Leave it empty to bind every interface.");

                // Two servers can't hold the same host port on the same interface; Docker would simply fail
                // to start the second one, long after the operator has left this dialog.
                var clash = await db.ServerInstances
                    .Where(s => s.Id != server.Id && s.LanPort == port)
                    .Select(s => new { s.DisplayName, s.LanBindAddress })
                    .ToListAsync(cancellationToken);

                var conflict = clash.FirstOrDefault(c => c.LanBindAddress == bind || c.LanBindAddress is null || bind is null);
                if (conflict is not null)
                    throw new ArgumentException($"Port {port} is already published by '{conflict.DisplayName}'.");
            }

            // Only a publishing change needs the container rebuilt.
            var republish = server.LanPort != port || server.LanBindAddress != bind;

            server.LanPort = port;
            server.LanBindAddress = bind;
            server.IsPublic = port is not null;
            server.RoutedThroughProxy = command.RoutedThroughProxy;
            await db.SaveChangesAsync(cancellationToken);

            if (republish && server.IsManaged && server.ContainerName is not null)
            {
                await mediator.Send(new RecreateServerCommand(
                    server.Id, server.Memory, server.Version, server.JavaVersion, server.UseAikarFlags,
                    server.AllowAnyClientVersion, server.JvmArgs, server.ExtraEnv), cancellationToken);
            }

            return Unit.Value;
        }
    }
}
