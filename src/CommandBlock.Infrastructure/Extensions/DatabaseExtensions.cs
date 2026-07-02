using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommandBlock.Infrastructure.Extensions
{
    /// <summary>CommandBlock runs on PostgreSQL. Migrations live in the default DbContext assembly
    /// (CommandBlock.Infrastructure).</summary>
    public static class DatabaseExtensions
    {
        public static IServiceCollection AddCommandBlockDatabase(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("CommandBlockDatabase");
            services.AddDbContext<CommandBlockDbContext>(options => options.ConfigureCommandBlockProvider(connectionString));
            return services;
        }

        public static DbContextOptionsBuilder ConfigureCommandBlockProvider(this DbContextOptionsBuilder options, string? connectionString)
        {
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null));
            return options;
        }
    }
}
