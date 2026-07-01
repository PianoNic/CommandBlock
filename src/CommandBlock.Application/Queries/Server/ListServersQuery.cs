using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Dtos.Server;
using CommandBlock.Application.Mappings.Server;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Queries.Server
{
    public record ListServersQuery : IQuery<IReadOnlyList<ServerInstanceDto>>;

    public class ListServersQueryHandler(CommandBlockDbContext db, IServerStatusService statusService)
        : IQueryHandler<ListServersQuery, IReadOnlyList<ServerInstanceDto>>
    {
        public async ValueTask<IReadOnlyList<ServerInstanceDto>> Handle(ListServersQuery query, CancellationToken cancellationToken)
        {
            var rows = await db.ServerInstances
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync(cancellationToken);

            var status = (await statusService.GetAllAsync(cancellationToken)).ToDictionary(s => s.Id);

            return rows.Select(s =>
            {
                status.TryGetValue(s.Id, out var st);
                return s.ToDto(st?.State, st?.PlayersOnline, st?.PlayersMax);
            }).ToList();
        }
    }
}
