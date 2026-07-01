using Mediator;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Extensions;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.InnerUser
{
    public record DeleteInnerUserCommand(Guid InstanceId, string Name) : ICommand;

    public class DeleteInnerUserCommandHandler(CommandBlockDbContext db, ISecretsVaultService vault, IInnerUserServiceResolver resolver, ConfigManagedGuard guard) : ICommandHandler<DeleteInnerUserCommand>
    {
        public async ValueTask<Unit> Handle(DeleteInnerUserCommand command, CancellationToken cancellationToken)
        {
            await guard.EnsureMutableAsync(db, command.InstanceId, cancellationToken);
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            await resolver.Resolve(target.Engine).DeleteAsync(target, command.Name, cancellationToken);
            return Unit.Value;
        }
    }
}
