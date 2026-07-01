using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using CommandBlock.Application.Dtos.Settings;
using CommandBlock.Application.Options;
using CommandBlock.Application.Queries.SupportedDatabase;

namespace CommandBlock.Application.Queries.Settings
{
    public record GetSettingsQuery : IQuery<SettingsDto>;

    public class GetSettingsQueryHandler(IOptions<CommandBlockOptions> options, IConfiguration configuration, IMediator mediator) : IQueryHandler<GetSettingsQuery, SettingsDto>
    {
        public async ValueTask<SettingsDto> Handle(GetSettingsQuery query, CancellationToken cancellationToken)
        {
            var supported = await mediator.Send(new GetSupportedDatabasesQuery(), cancellationToken);

            var ranges = options.Value.PortRanges
                .Select(kv =>
                {
                    var range = PortRange.Parse(kv.Key, kv.Value);
                    return new PortRangeDto { Engine = kv.Key, Start = range.Start, End = range.End };
                })
                .OrderBy(r => r.Engine, StringComparer.Ordinal)
                .ToList();

            return new SettingsDto
            {
                PortRanges = ranges,
                SupportedEngines = supported,
                VaultMasterKeyConfigured = !string.IsNullOrWhiteSpace(configuration["Vault:MasterKey"]),
            };
        }
    }
}
