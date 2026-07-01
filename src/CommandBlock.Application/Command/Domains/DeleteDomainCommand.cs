using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.Domains
{
    public record DeleteDomainCommand(Guid Id) : ICommand;

    public class DeleteDomainCommandHandler(CommandBlockDbContext db, IActivityLogger activity)
        : ICommandHandler<DeleteDomainCommand>
    {
        public async ValueTask<Unit> Handle(DeleteDomainCommand command, CancellationToken cancellationToken)
        {
            var entry = await db.Domains.FirstOrDefaultAsync(d => d.Id == command.Id, cancellationToken);
            if (entry is null) return Unit.Value; // idempotent - already gone

            db.Domains.Remove(entry);
            await db.SaveChangesAsync(cancellationToken);
            await activity.LogAsync("domain.remove", entry.Name, null, null, null, cancellationToken);
            return Unit.Value;
        }
    }
}
