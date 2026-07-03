using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.Server
{
    /// <summary>Renames a server's display name. Lightweight - just a DB write; the hostname (the
    /// router's key) is immutable, so nothing about routing or the container changes.</summary>
    public record RenameServerCommand(Guid ServerId, string DisplayName) : ICommand;

    public class RenameServerCommandHandler(CommandBlockDbContext db, IActivityLogger activity) : ICommandHandler<RenameServerCommand>
    {
        public async ValueTask<Unit> Handle(RenameServerCommand command, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.FirstOrDefaultAsync(s => s.Id == command.ServerId, cancellationToken)
                ?? throw new ServerNotFoundException(command.ServerId);

            var name = command.DisplayName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Display name is required.", nameof(command));

            server.DisplayName = name;
            await db.SaveChangesAsync(cancellationToken);
            await activity.LogAsync("server.rename", server.ContainerName ?? name, server.Id, server.ServerType, $"name={name}", cancellationToken);
            return Unit.Value;
        }
    }
}
