using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure;

namespace CommandBlock.Application.Command.Server
{
    /// <summary>Sets a server's wake-on-connect behaviour. Lightweight - just a DB write; the router
    /// reads these fields live per connection, so there's no restart or container recreate.</summary>
    public record UpdateWakeCommand(Guid ServerId, bool WakeOnConnect, int WakeQueueSeconds) : ICommand;

    public class UpdateWakeCommandHandler(CommandBlockDbContext db) : ICommandHandler<UpdateWakeCommand>
    {
        public async ValueTask<Unit> Handle(UpdateWakeCommand command, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.FirstOrDefaultAsync(s => s.Id == command.ServerId, cancellationToken)
                ?? throw new ServerNotFoundException(command.ServerId);

            server.WakeOnConnect = command.WakeOnConnect;
            server.WakeQueueSeconds = Math.Clamp(command.WakeQueueSeconds, 0, 28);
            await db.SaveChangesAsync(cancellationToken);
            return Unit.Value;
        }
    }
}
