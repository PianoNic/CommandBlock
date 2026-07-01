using CommandBlock.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommandBlock.Infrastructure.DBConfigurations
{
    public class BackupEntryConfiguration : IEntityTypeConfiguration<BackupEntry>
    {
        public void Configure(EntityTypeBuilder<BackupEntry> builder)
        {
            // Backups are always listed for one server, newest first.
            builder.HasIndex(b => b.ServerId);
        }
    }
}
