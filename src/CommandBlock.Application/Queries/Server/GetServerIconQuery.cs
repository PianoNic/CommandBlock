using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure;

namespace CommandBlock.Application.Queries.Server
{
    public record GetServerIconQuery(Guid ServerId) : IQuery<byte[]?>;

    public class GetServerIconQueryHandler(CommandBlockDbContext db) : IQueryHandler<GetServerIconQuery, byte[]?>
    {
        public async ValueTask<byte[]?> Handle(GetServerIconQuery query, CancellationToken cancellationToken)
            => await db.ServerInstances.AsNoTracking()
                .Where(s => s.Id == query.ServerId)
                .Select(s => s.IconPng)
                .FirstOrDefaultAsync(cancellationToken);
    }
}
