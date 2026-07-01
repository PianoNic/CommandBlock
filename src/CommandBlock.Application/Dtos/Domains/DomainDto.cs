namespace CommandBlock.Application.Dtos.Domains
{
    public record DomainDto
    {
        public required Guid Id { get; init; }
        /// <summary>The root domain, e.g. "gaggao.com".</summary>
        public required string Name { get; init; }
        public required DateTime CreatedAt { get; init; }
    }

    /// <summary>Request body for adding a domain.</summary>
    public record AddDomainDto
    {
        public required string Name { get; init; }
    }
}
