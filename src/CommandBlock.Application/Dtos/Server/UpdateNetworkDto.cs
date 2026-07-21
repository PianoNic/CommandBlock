namespace CommandBlock.Application.Dtos.Server
{
    /// <summary>Request body for how a server is reachable - through the router, or directly on its own
    /// host port. The two are exclusive; whichever the flag doesn't select is cleared.</summary>
    public record UpdateNetworkDto
    {
        /// <summary>True to reach the server through the router by hostname, false to publish a port instead.</summary>
        public required bool RoutedThroughProxy { get; init; }
        /// <summary>Host port to publish the server on. Required when not routed, ignored when routed.</summary>
        public int? LanPort { get; init; }
        /// <summary>Host interface to bind the port to. Empty binds all of them; a private address keeps it off
        /// a public interface.</summary>
        public string? LanBindAddress { get; init; }
        /// <summary>Hostname to route by. Only needed when moving a server onto the router that was created
        /// without one; omit to keep the existing hostname.</summary>
        public string? Hostname { get; init; }
    }
}
