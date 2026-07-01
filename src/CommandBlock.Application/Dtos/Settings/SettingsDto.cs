using CommandBlock.Application.Dtos.SupportedDatabase;

namespace CommandBlock.Application.Dtos.Settings
{
    public record SettingsDto
    {
        public required IReadOnlyList<PortRangeDto> PortRanges { get; init; }
        public required IReadOnlyList<SupportedDatabaseDto> SupportedEngines { get; init; }
        public required bool VaultMasterKeyConfigured { get; init; }
    }
}
