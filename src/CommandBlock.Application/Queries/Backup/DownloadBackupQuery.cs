using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Queries.Backup
{
    /// <summary>An open read stream for a backup archive plus its download filename. The caller
    /// (the API's File() result) streams and disposes it.</summary>
    public sealed record BackupDownload(Stream Content, string FileName);

    public record DownloadBackupQuery(Guid BackupId) : IQuery<BackupDownload?>;

    public class DownloadBackupQueryHandler(CommandBlockDbContext db, IBackupStorage storage)
        : IQueryHandler<DownloadBackupQuery, BackupDownload?>
    {
        public async ValueTask<BackupDownload?> Handle(DownloadBackupQuery query, CancellationToken cancellationToken)
        {
            var backup = await db.BackupEntries.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == query.BackupId, cancellationToken);
            if (backup is null) return null;

            var stream = await storage.OpenReadAsync(backup.ObjectKey, cancellationToken);
            return new BackupDownload(stream, backup.FileName);
        }
    }
}
