namespace CommandBlock.Application.Dtos.Server
{
    /// <summary>Request body for renaming a server's display name (the hostname is immutable).</summary>
    public record RenameServerDto
    {
        public required string DisplayName { get; init; }
    }
}
