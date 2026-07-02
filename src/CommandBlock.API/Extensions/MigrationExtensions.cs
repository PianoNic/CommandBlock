using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure;
using Npgsql;

namespace CommandBlock.API.Extensions
{
    public static class MigrationExtensions
    {
        public static WebApplication ApplyMigrations(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CommandBlockDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Migrations");

            const int maxAttempts = 30;
            var delay = TimeSpan.FromSeconds(2);

            // Postgres is networked, so it may not be ready the instant the app starts - retry the
            // migrate through a short wait-for-database loop.
            var retryOnFailure = true;

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    db.Database.Migrate();
                    return app;
                }
                catch (Exception ex) when (retryOnFailure
                    && (ex is NpgsqlException || ex.InnerException is NpgsqlException)
                    && attempt < maxAttempts)
                {
                    logger.LogWarning("Database not reachable (attempt {Attempt}/{Max}): {Message}. Retrying in {Delay}s.",
                        attempt, maxAttempts, ex.GetBaseException().Message, delay.TotalSeconds);
                    Thread.Sleep(delay);
                }
            }
        }
    }
}
