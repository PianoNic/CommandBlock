namespace CommandBlock.Application.Dtos.Files
{
    public record WriteFileDto
    {
        public required string Path { get; init; }
        public required string Content { get; init; }
    }

    public record PathDto
    {
        public required string Path { get; init; }
    }

    public record RenameFileDto
    {
        public required string From { get; init; }
        public required string To { get; init; }
    }
}
