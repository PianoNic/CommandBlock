using Microsoft.Extensions.Diagnostics.HealthChecks;
using CommandBlock.Infrastructure;

namespace CommandBlock.API.Health
{
    /// <summary>Readiness probe: reports healthy only when the app can reach its Postgres database.
    /// Backs the anonymous <c>/health</c> endpoint used by load balancers and container orchestrators.</summary>
    public sealed class DatabaseHealthCheck(CommandBlockDbContext db) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                return await db.Database.CanConnectAsync(cancellationToken)
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy("Database unreachable.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Database check failed.", ex);
            }
        }
    }
}
