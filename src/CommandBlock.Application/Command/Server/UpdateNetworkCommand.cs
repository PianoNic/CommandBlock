using System.Net;
using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure;

namespace CommandBlock.Application.Command.Server
{
    /// <summary>Sets how a server is reachable: through the router by hostname, directly on a published
    /// host port, or both. Publishing is baked into the container at create time, so a change to the port
    /// or its bind address recreates the container (the world is bind-mounted by name and survives).
    /// Toggling routing alone is just a DB write - the router resolves per connection.</summary>
    public record UpdateNetworkCommand(Guid ServerId, int? LanPort, string? LanBindAddress, bool RoutedThroughProxy) : ICommand;

    public class UpdateNetworkCommandHandler(CommandBlockDbContext db, IMediator mediator) : ICommandHandler<UpdateNetworkCommand>
    {
        public async ValueTask<Unit> Handle(UpdateNetworkCommand command, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.FirstOrDefaultAsync(s => s.Id == command.ServerId, cancellationToken)
                ?? throw new ServerNotFoundException(command.ServerId);

            var port = command.LanPort;
            var bind = string.IsNullOrWhiteSpace(command.LanBindAddress) ? null : command.LanBindAddress.Trim();

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

            if (port is null && !command.RoutedThroughProxy)
                throw new ArgumentException("The server would be unreachable: give it a port, or leave it on the router.");

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
