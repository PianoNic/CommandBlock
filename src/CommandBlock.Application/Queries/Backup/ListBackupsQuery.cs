using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Dtos.Backup;
using CommandBlock.Application.Mappings.Backup;
using CommandBlock.Infrastructure;

namespace CommandBlock.Application.Queries.Backup
{
    public record ListBackupsQuery(Guid? InstanceId = null) : IQuery<IReadOnlyList<BackupEntryDto>>;

    public class ListBackupsQueryHandler(CommandBlockDbContext db)
        : IQueryHandler<ListBackupsQuery, IReadOnlyList<BackupEntryDto>>
    {
        public async ValueTask<IReadOnlyList<BackupEntryDto>> Handle(ListBackupsQuery query, CancellationToken cancellationToken)
        {
            var q = db.BackupEntries.AsQueryable();
            if (query.InstanceId is { } id) q = q.Where(b => b.InstanceId == id);

            var rows = await q
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync(cancellationToken);
            return rows.Select(b => b.ToDto()).ToList();
        }
    }
}
