namespace CommandBlock.API.Routing
{
    /// <summary>Where a routed connection should be forwarded, plus the identity needed to wake a
    /// stopped server. Host is a container name on the shared Docker network.</summary>
    public sealed record RouteTarget(string Host, int Port, Guid ServerId, string ContainerId, string DisplayName, bool WakeOnConnect, int WakeQueueSeconds);

    /// <summary>Resolves the hostname a player typed into a backend server to forward to.</summary>
    public interface IServerRouteResolver
    {
        Task<RouteTarget?> ResolveAsync(string hostname, CancellationToken cancellationToken);
    }
}
