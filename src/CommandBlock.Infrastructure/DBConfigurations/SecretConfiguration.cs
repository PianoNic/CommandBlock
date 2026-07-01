using CommandBlock.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommandBlock.Infrastructure.DBConfigurations
{
    public class SecretConfiguration : IEntityTypeConfiguration<Secret>
    {
        public void Configure(EntityTypeBuilder<Secret> builder)
        {
            builder.HasIndex(s => s.Name).IsUnique();
        }
    }
}
