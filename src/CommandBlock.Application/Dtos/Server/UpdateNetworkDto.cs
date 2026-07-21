namespace CommandBlock.Application.Dtos.Server
{
    /// <summary>Request body for how a server is reachable: the router, a published host port, or both.</summary>
    public record UpdateNetworkDto
    {
        /// <summary>Host port to publish the server on for direct access. Null publishes nothing.</summary>
        public int? LanPort { get; init; }
        /// <summary>Host interface to bind the port to. Empty binds all of them; a private address keeps it off
        /// a public interface.</summary>
        public string? LanBindAddress { get; init; }
        /// <summary>Whether the router answers for this server's hostname.</summary>
        public required bool RoutedThroughProxy { get; init; }
    }
}
