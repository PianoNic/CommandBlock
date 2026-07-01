using CommandBlock.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommandBlock.Infrastructure.DBConfigurations
{
    public class ServerInstanceConfiguration : IEntityTypeConfiguration<ServerInstance>
    {
        public void Configure(EntityTypeBuilder<ServerInstance> builder)
        {
            // The router keys its routing table on Hostname, so it must be unique across servers.
            builder.HasIndex(s => s.Hostname)
                .IsUnique();

            // Externally-registered servers have no container - filter the unique index so those
            // nullable rows don't collide with each other.
            builder.HasIndex(s => s.ContainerName)
                .IsUnique()
                .HasFilter("\"ContainerName\" IS NOT NULL");
        }
    }
}
