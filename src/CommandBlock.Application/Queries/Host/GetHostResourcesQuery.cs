using System.Text.RegularExpressions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Queries.Host
{
    /// <summary>Host memory picture for the create dialog's slider: total physical RAM, how much is
    /// already committed to existing servers (their configured caps), and what's left to hand out.</summary>
    public sealed record HostResourcesDto(long TotalMemoryBytes, long AllocatedMemoryBytes, long AvailableMemoryBytes);

    public record GetHostResourcesQuery : IQuery<HostResourcesDto>;

    public partial class GetHostResourcesQueryHandler(CommandBlockDbContext db, IDockerServiceResolver dockerResolver)
        : IQueryHandler<GetHostResourcesQuery, HostResourcesDto>
    {
        public async ValueTask<HostResourcesDto> Handle(GetHostResourcesQuery query, CancellationToken cancellationToken)
        {
            var total = await dockerResolver.Resolve(null).GetHostMemoryTotalBytesAsync(cancellationToken);

            var mems = await db.ServerInstances.AsNoTracking().Select(s => s.Memory).ToListAsync(cancellationToken);
            var allocated = mems.Sum(ParseMemoryBytes);

            var available = total > 0 ? Math.Max(0, total - allocated) : 0;
            return new HostResourcesDto(total, allocated, available);
        }

        /// <summary>Parses an itzg MEMORY value ("4G", "512M", "2048") into bytes. Unknown -> 0.</summary>
        internal static long ParseMemoryBytes(string? mem)
        {
            var m = MemoryRegex().Match(mem ?? "");
            if (!m.Success) return 0;
            var n = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return (m.Groups[2].Value.ToUpperInvariant()) switch
            {
                "G" => (long)(n * 1024 * 1024 * 1024),
                "K" => (long)(n * 1024),
                "M" => (long)(n * 1024 * 1024),
                _ => (long)(n * 1024 * 1024), // bare number = MB (itzg default unit)
            };
        }

        [GeneratedRegex(@"^\s*(\d+(?:\.\d+)?)\s*([gmkGMK]?)")]
        private static partial Regex MemoryRegex();
    }
}
