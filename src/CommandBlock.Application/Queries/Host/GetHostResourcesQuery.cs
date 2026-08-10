using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Command.Server;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Queries.Host
{
    /// <summary>Host memory picture for the create dialog's slider: total physical RAM, how much is
    /// already committed to existing servers (their configured caps), and what's left to hand out.</summary>
    public sealed record HostResourcesDto(long TotalMemoryBytes, long AllocatedMemoryBytes, long AvailableMemoryBytes);

    public record GetHostResourcesQuery : IQuery<HostResourcesDto>;

    public partial class GetHostResourcesQueryHandler(CommandBlockDbContext db, IDockerService docker)
        : IQueryHandler<GetHostResourcesQuery, HostResourcesDto>
    {
        public async ValueTask<HostResourcesDto> Handle(GetHostResourcesQuery query, CancellationToken cancellationToken)
        {
            var total = await docker.GetHostMemoryTotalBytesAsync(cancellationToken);

            var mems = await db.ServerInstances.AsNoTracking().Select(s => s.Memory).ToListAsync(cancellationToken);
            var allocated = mems.Sum(ServerContainerSpec.ParseMemoryBytes);

            var available = total > 0 ? Math.Max(0, total - allocated) : 0;
            return new HostResourcesDto(total, allocated, available);
        }

    }
}
