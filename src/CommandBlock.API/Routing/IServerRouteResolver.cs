namespace CommandBlock.API.Routing
{
    /// <summary>Where a routed connection should be forwarded, plus the identity needed to wake a
    /// stopped server. Host is a container name on the shared Docker network.</summary>
    public sealed record RouteTarget(string Host, int Port, Guid ServerId, string ContainerId, string DisplayName, bool WakeOnConnect, int WakeQueueSeconds, string ServerType)
    {
        /// <summary>Mod loaders whose clients negotiate a mod list on join. The limbo replays a captured vanilla
        /// handshake and can't satisfy that, so these servers must use the protocol-agnostic hold instead.</summary>
        public bool IsModded => ServerType is "FORGE" or "NEOFORGE" or "AUTO_CURSEFORGE" or "CURSEFORGE" or "FTBA" or "MODRINTH";
    }

    /// <summary>Resolves the hostname a player typed into a backend server to forward to.</summary>
    public interface IServerRouteResolver
    {
        Task<RouteTarget?> ResolveAsync(string hostname, CancellationToken cancellationToken);
    }
}
