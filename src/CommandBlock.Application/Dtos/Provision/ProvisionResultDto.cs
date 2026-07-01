using CommandBlock.Application.Dtos.Database;
using CommandBlock.Application.Dtos.InnerUser;

namespace CommandBlock.Application.Dtos.Provision
{
    public record ProvisionResultDto
    {
        public required ProvisionedDatabaseDto Instance { get; init; }
        public required IReadOnlyList<string> Databases { get; init; }
        public required IReadOnlyList<InnerUserPasswordDto> Users { get; init; }
    }
}
