using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Dtos.BackupSchedule;
using CommandBlock.Application.Mappings.BackupSchedule;
using CommandBlock.Infrastructure;

namespace CommandBlock.Application.Queries.BackupSchedule
{
    public record ListBackupSchedulesQuery(Guid ServerId) : IQuery<IReadOnlyList<BackupScheduleDto>>;

    public class ListBackupSchedulesQueryHandler(CommandBlockDbContext db)
        : IQueryHandler<ListBackupSchedulesQuery, IReadOnlyList<BackupScheduleDto>>
    {
        public async ValueTask<IReadOnlyList<BackupScheduleDto>> Handle(ListBackupSchedulesQuery query, CancellationToken cancellationToken)
        {
            var rows = await db.BackupSchedules
                .AsNoTracking()
                .Where(s => s.ServerId == query.ServerId)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync(cancellationToken);
            return rows.Select(s => s.ToDto()).ToList();
        }
    }
}
