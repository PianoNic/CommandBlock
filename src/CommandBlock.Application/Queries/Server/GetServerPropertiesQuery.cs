using Mediator;
using CommandBlock.Application.Command.Server;
using CommandBlock.Application.Dtos.Server;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Queries.Server
{
    public record GetServerPropertiesQuery(Guid ServerId) : IQuery<ServerPropertiesDto>;

    public class GetServerPropertiesQueryHandler(IServerFilesService files)
        : IQueryHandler<GetServerPropertiesQuery, ServerPropertiesDto>
    {
        private static readonly ServerPropertiesDto Unavailable = new() { Available = false };

        public async ValueTask<ServerPropertiesDto> Handle(GetServerPropertiesQuery query, CancellationToken cancellationToken)
        {
            try
            {
                var file = await files.ReadTextAsync(query.ServerId, "server.properties", cancellationToken);
                return ServerPropertiesFile.ToDto(file.Content);
            }
            catch
            {
                // No container yet, or server.properties not generated (server never started).
                return Unavailable;
            }
        }
    }
}
