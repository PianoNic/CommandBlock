using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Dtos.BackupSchedule;
using CommandBlock.Application.Mappings.BackupSchedule;
using CommandBlock.Infrastructure;

namespace CommandBlock.Application.Queries.BackupSchedule
{
    public record ListBackupSchedulesQuery(Guid? InstanceId) : IQuery<IReadOnlyList<BackupScheduleDto>>;

    public class ListBackupSchedulesQueryHandler(CommandBlockDbContext db)
        : IQueryHandler<ListBackupSchedulesQuery, IReadOnlyList<BackupScheduleDto>>
    {
        public async ValueTask<IReadOnlyList<BackupScheduleDto>> Handle(ListBackupSchedulesQuery query, CancellationToken cancellationToken)
        {
            var q = db.BackupSchedules.AsNoTracking().OrderByDescending(s => s.CreatedAt).AsQueryable();
            if (query.InstanceId is { } id) q = q.Where(s => s.InstanceId == id);
            var rows = await q.ToListAsync(cancellationToken);
            return rows.Select(s => s.ToDto()).ToList();
        }
    }
}
