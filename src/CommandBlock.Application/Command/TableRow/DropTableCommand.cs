using Mediator;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Extensions;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.TableRow
{
    public record DropTableCommand(Guid InstanceId, string Database, string Table) : ICommand;

    public class DropTableCommandHandler(CommandBlockDbContext db, ISecretsVaultService vault, IInnerSchemaServiceResolver resolver) : ICommandHandler<DropTableCommand>
    {
        public async ValueTask<Unit> Handle(DropTableCommand command, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            await resolver.Resolve(target.Engine).DropTableAsync(target, command.Database, command.Table, cancellationToken);
            return Unit.Value;
        }
    }
}
