using CommandBlock.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommandBlock.Infrastructure.DBConfigurations
{
    public class DomainEntryConfiguration : IEntityTypeConfiguration<DomainEntry>
    {
        public void Configure(EntityTypeBuilder<DomainEntry> builder)
        {
            builder.HasIndex(d => d.Name).IsUnique();
        }
    }
}
