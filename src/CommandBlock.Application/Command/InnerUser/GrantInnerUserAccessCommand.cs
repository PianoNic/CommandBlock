using Mediator;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Extensions;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.InnerUser
{
    public record GrantInnerUserAccessCommand(Guid InstanceId, string User, string Database) : ICommand;

    public class GrantInnerUserAccessCommandHandler(CommandBlockDbContext db, ISecretsVaultService vault, IInnerUserServiceResolver resolver, ConfigManagedGuard guard) : ICommandHandler<GrantInnerUserAccessCommand>
    {
        public async ValueTask<Unit> Handle(GrantInnerUserAccessCommand command, CancellationToken cancellationToken)
        {
            await guard.EnsureMutableAsync(db, command.InstanceId, cancellationToken);
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            await resolver.Resolve(target.Engine).GrantAccessAsync(target, command.User, command.Database, cancellationToken);
            return Unit.Value;
        }
    }
}
