using Mediator;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Extensions;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.InnerDatabase
{
    public record DropInnerDatabaseCommand(Guid InstanceId, string Name) : ICommand;

    public class DropInnerDatabaseCommandHandler(CommandBlockDbContext db, ISecretsVaultService vault, IInnerDatabaseServiceResolver resolver, ConfigManagedGuard guard) : ICommandHandler<DropInnerDatabaseCommand>
    {
        public async ValueTask<Unit> Handle(DropInnerDatabaseCommand command, CancellationToken cancellationToken)
        {
            await guard.EnsureMutableAsync(db, command.InstanceId, cancellationToken);
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            await resolver.Resolve(target.Engine).DropAsync(target, command.Name, cancellationToken);
            return Unit.Value;
        }
    }
}
