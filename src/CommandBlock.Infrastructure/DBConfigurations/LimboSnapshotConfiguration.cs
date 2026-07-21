using CommandBlock.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommandBlock.Infrastructure.DBConfigurations
{
    public class LimboSnapshotConfiguration : IEntityTypeConfiguration<LimboSnapshot>
    {
        public void Configure(EntityTypeBuilder<LimboSnapshot> builder)
        {
            // One snapshot per protocol version - the limbo looks it up by the joining client's protocol.
            builder.HasIndex(s => s.Protocol).IsUnique();
        }
    }
}
