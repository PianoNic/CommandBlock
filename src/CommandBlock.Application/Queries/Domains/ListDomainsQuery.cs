using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Dtos.Domains;
using CommandBlock.Infrastructure;

namespace CommandBlock.Application.Queries.Domains
{
    public record ListDomainsQuery : IQuery<IReadOnlyList<DomainDto>>;

    public class ListDomainsQueryHandler(CommandBlockDbContext db)
        : IQueryHandler<ListDomainsQuery, IReadOnlyList<DomainDto>>
    {
        public async ValueTask<IReadOnlyList<DomainDto>> Handle(ListDomainsQuery query, CancellationToken cancellationToken)
        {
            return await db.Domains
                .OrderBy(d => d.Name)
                .Select(d => new DomainDto { Id = d.Id, Name = d.Name, CreatedAt = d.CreatedAt })
                .ToListAsync(cancellationToken);
        }
    }
}
