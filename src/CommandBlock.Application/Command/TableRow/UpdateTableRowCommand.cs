using Mediator;
using CommandBlock.Application.Dtos.TableRow;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Extensions;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.TableRow
{
    public record UpdateTableRowCommand(Guid InstanceId, string Database, string Table, UpdateRowDto Body) : ICommand;

    public class UpdateTableRowCommandHandler(CommandBlockDbContext db, ISecretsVaultService vault, IInnerSchemaServiceResolver resolver) : ICommandHandler<UpdateTableRowCommand>
    {
        public async ValueTask<Unit> Handle(UpdateTableRowCommand command, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            var request = new UpdateRowRequest(command.Body.Columns, command.Body.OriginalValues, command.Body.NewValues);
            await resolver.Resolve(target.Engine).UpdateRowAsync(target, command.Database, command.Table, request, cancellationToken);
            return Unit.Value;
        }
    }
}
