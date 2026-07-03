namespace CommandBlock.Application.Dtos.Server
{
    /// <summary>Request body for renaming a server's display name and/or hostname.</summary>
    public record RenameServerDto
    {
        public required string DisplayName { get; init; }
        /// <summary>The hostname players connect with (the router's routing key). Must be unique;
        /// normalized to lowercase.</summary>
        public required string Hostname { get; init; }
    }
}
