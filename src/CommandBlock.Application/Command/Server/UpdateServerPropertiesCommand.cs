using Mediator;
using CommandBlock.Application.Dtos.Server;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.Server
{
    public record UpdateServerPropertiesCommand(Guid ServerId, UpdateServerPropertiesDto Values) : ICommand;

    public class UpdateServerPropertiesCommandHandler(IServerFilesService files)
        : ICommandHandler<UpdateServerPropertiesCommand>
    {
        public async ValueTask<Unit> Handle(UpdateServerPropertiesCommand command, CancellationToken cancellationToken)
        {
            // Read the current file so we only touch the curated keys and keep everything else intact.
            var current = await files.ReadTextAsync(command.ServerId, "server.properties", cancellationToken);
            var merged = ServerPropertiesFile.ApplyUpdate(current.Content, command.Values);
            await files.WriteTextAsync(command.ServerId, "server.properties", merged, cancellationToken);
            return Unit.Value;
        }
    }
}
