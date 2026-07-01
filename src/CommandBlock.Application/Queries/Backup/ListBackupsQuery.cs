using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Dtos.Backup;
using CommandBlock.Application.Mappings.Backup;
using CommandBlock.Infrastructure;

namespace CommandBlock.Application.Queries.Backup
{
    public record ListBackupsQuery(Guid ServerId) : IQuery<IReadOnlyList<BackupEntryDto>>;

    public class ListBackupsQueryHandler(CommandBlockDbContext db)
        : IQueryHandler<ListBackupsQuery, IReadOnlyList<BackupEntryDto>>
    {
        public async ValueTask<IReadOnlyList<BackupEntryDto>> Handle(ListBackupsQuery query, CancellationToken cancellationToken)
        {
            var rows = await db.BackupEntries
                .AsNoTracking()
                .Where(b => b.ServerId == query.ServerId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync(cancellationToken);

            return rows.Select(b => b.ToDto()).ToList();
        }
    }
}
