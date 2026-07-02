using CommandBlock.Application.Dtos.Backup;

namespace CommandBlock.Application.Mappings.Backup
{
    public static class BackupEntryMappings
    {
        public static BackupEntryDto ToDto(this CommandBlock.Domain.BackupEntry b) => new()
        {
            Id = b.Id,
            ServerId = b.ServerId,
            FileName = b.FileName,
            SizeBytes = b.SizeBytes,
            Kind = b.Kind.ToString(),
            CreatedAt = b.CreatedAt,
        };
    }
}
