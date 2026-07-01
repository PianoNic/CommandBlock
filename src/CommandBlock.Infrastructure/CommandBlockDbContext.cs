using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using CommandBlock.Domain;
using CommandBlock.Infrastructure.Extensions;

namespace CommandBlock.Infrastructure
{
    public class CommandBlockDbContext(DbContextOptions<CommandBlockDbContext> options) : DbContext(options)
    {
        public DbSet<Secret> Secrets => Set<Secret>();
        public DbSet<ServerInstance> ServerInstances => Set<ServerInstance>();
        public DbSet<ActivityEntry> ActivityEntries => Set<ActivityEntry>();
        public DbSet<BackupEntry> BackupEntries => Set<BackupEntry>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(CommandBlockDbContext).Assembly);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ApplySaveChangesGuards();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            ApplySaveChangesGuards();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void ApplySaveChangesGuards()
        {
            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            {
                if (entry.State == EntityState.Modified)
                {
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                }
            }
        }
    }

    public class CommandBlockDbContextFactory : IDesignTimeDbContextFactory<CommandBlockDbContext>
    {
        public CommandBlockDbContext CreateDbContext(string[] args)
        {
            // Used by the EF Core CLI. Provider and connection string are read straight from
            // the environment so `dotnet ef` targets the same database the app would at runtime.
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CommandBlockDatabase");
            var provider = DatabaseExtensions.ResolveProvider(Environment.GetEnvironmentVariable("Database__Provider"), connectionString);

            var optionsBuilder = new DbContextOptionsBuilder<CommandBlockDbContext>();
            optionsBuilder.ConfigureCommandBlockProvider(provider, connectionString);
            return new CommandBlockDbContext(optionsBuilder.Options);
        }
    }
}
