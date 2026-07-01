using Mediator;
using CommandBlock.Application.Dtos.TableRow;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Extensions;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.TableRow
{
    public record DeleteTableRowCommand(Guid InstanceId, string Database, string Table, DeleteRowDto Body) : ICommand;

    public class DeleteTableRowCommandHandler(CommandBlockDbContext db, ISecretsVaultService vault, IInnerSchemaServiceResolver resolver) : ICommandHandler<DeleteTableRowCommand>
    {
        public async ValueTask<Unit> Handle(DeleteTableRowCommand command, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            var request = new DeleteRowRequest(command.Body.Columns, command.Body.OriginalValues);
            await resolver.Resolve(target.Engine).DeleteRowAsync(target, command.Database, command.Table, request, cancellationToken);
            return Unit.Value;
        }
    }
}
