using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure;

namespace CommandBlock.Application.Command.Server
{
    /// <summary>Sets a server's wake-on-connect and auto-sleep settings. Lightweight - just a DB write;
    /// the router reads wake fields live per connection and the idle monitor reads sleep fields per
    /// sweep, so there's no restart or container recreate.</summary>
    public record UpdateWakeCommand(Guid ServerId, bool WakeOnConnect, int WakeQueueSeconds, bool AutoSleepEnabled, int AutoSleepIdleMinutes) : ICommand;

    public class UpdateWakeCommandHandler(CommandBlockDbContext db) : ICommandHandler<UpdateWakeCommand>
    {
        public async ValueTask<Unit> Handle(UpdateWakeCommand command, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.FirstOrDefaultAsync(s => s.Id == command.ServerId, cancellationToken)
                ?? throw new ServerNotFoundException(command.ServerId);

            server.WakeOnConnect = command.WakeOnConnect;
            // 0 means "tell them to reconnect". The old 25s ceiling existed because a silent hold died at the
            // client's ~30s login timeout; keep-alive plugin requests removed that limit, so a modpack that takes
            // minutes to boot can now be waited out. The router still caps this at Router:MaxHoldSeconds.
            server.WakeQueueSeconds = Math.Clamp(command.WakeQueueSeconds, 0, 600);
            server.AutoSleepEnabled = command.AutoSleepEnabled;
            server.AutoSleepIdleMinutes = Math.Clamp(command.AutoSleepIdleMinutes, 1, 1440);
            await db.SaveChangesAsync(cancellationToken);
            return Unit.Value;
        }
    }
}
