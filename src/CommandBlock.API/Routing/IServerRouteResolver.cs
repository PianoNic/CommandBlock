namespace CommandBlock.API.Routing
{
    /// <summary>Where a routed connection should be forwarded: a backend host reachable from the
    /// control plane (a container name on the shared Docker network) and its Minecraft port.</summary>
    public sealed record RouteTarget(string Host, int Port);

    /// <summary>Resolves the hostname a player typed into a backend server to forward to.</summary>
    public interface IServerRouteResolver
    {
        Task<RouteTarget?> ResolveAsync(string hostname, CancellationToken cancellationToken);
    }
}
