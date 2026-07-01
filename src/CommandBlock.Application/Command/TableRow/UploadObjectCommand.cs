using Mediator;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Extensions;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.TableRow
{
    // Object/blob stores (SeaweedFS, Azurite): upload (or replace) an object by key. The content is
    // streamed straight from the request, so the file never has to be fully buffered by us.
    public record UploadObjectCommand(Guid InstanceId, string Database, string Key, Stream Content, string? ContentType) : ICommand;

    public class UploadObjectCommandHandler(CommandBlockDbContext db, ISecretsVaultService vault, IInnerSchemaServiceResolver resolver) : ICommandHandler<UploadObjectCommand>
    {
        public async ValueTask<Unit> Handle(UploadObjectCommand command, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            await resolver.Resolve(target.Engine).UploadObjectAsync(target, command.Database, command.Key, command.Content, command.ContentType, cancellationToken);
            return Unit.Value;
        }
    }
}
