using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.Server
{
    /// <summary>Renames a server's display name and hostname. Lightweight - just a DB write; the router
    /// resolves the hostname live per connection and dials the backend by container name, so a change
    /// reroutes on the next join without touching the container.</summary>
    public record RenameServerCommand(Guid ServerId, string DisplayName, string Hostname) : ICommand;

    public class RenameServerCommandHandler(CommandBlockDbContext db, IActivityLogger activity) : ICommandHandler<RenameServerCommand>
    {
        public async ValueTask<Unit> Handle(RenameServerCommand command, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.FirstOrDefaultAsync(s => s.Id == command.ServerId, cancellationToken)
                ?? throw new ServerNotFoundException(command.ServerId);

            var name = command.DisplayName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Display name is required.", nameof(command));

            var hostname = command.Hostname?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(hostname))
                throw new ArgumentException("Hostname is required.", nameof(command));
            if (await db.ServerInstances.AnyAsync(s => s.Hostname == hostname && s.Id != command.ServerId, cancellationToken))
                throw new InvalidOperationException($"A server with hostname '{hostname}' already exists.");

            server.DisplayName = name;
            server.Hostname = hostname;
            await db.SaveChangesAsync(cancellationToken);
            await activity.LogAsync("server.rename", server.ContainerName ?? name, server.Id, server.ServerType, $"name={name}, host={hostname}", cancellationToken);
            return Unit.Value;
        }
    }
}
