using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure;

namespace CommandBlock.API.Routing
{
    /// <summary>Resolves a route from the <c>ServerInstances</c> table. The backend host is the
    /// server's container name, which Docker's embedded DNS resolves on the shared network the
    /// control plane and provisioned servers both sit on. Servers without a container (externally
    /// registered, not yet created) don't route.</summary>
    public class DbServerRouteResolver(CommandBlockDbContext db) : IServerRouteResolver
    {
        public async Task<RouteTarget?> ResolveAsync(string hostname, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances
                .AsNoTracking()
                .Where(s => s.Hostname == hostname && s.ContainerName != null)
                .Select(s => new { s.ContainerName, s.Port })
                .FirstOrDefaultAsync(cancellationToken);

            return server is null ? null : new RouteTarget(server.ContainerName!, server.Port);
        }
    }
}
