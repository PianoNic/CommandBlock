using System.Text;
using System.Text.RegularExpressions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Dtos.Server;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Queries.Server
{
    /// <summary>Lists the players currently online via RCON <c>list</c>. Read on demand only (not
    /// polled) so it doesn't spam the server console the way frequent RCON calls would.</summary>
    public record ListPlayersQuery(Guid ServerId) : IQuery<PlayerListDto>;

    public partial class ListPlayersQueryHandler(CommandBlockDbContext db, IDockerService docker)
        : IQueryHandler<ListPlayersQuery, PlayerListDto>
    {
        private static readonly PlayerListDto Empty = new() { Online = 0, Max = 0, Players = [], Reachable = false };

        public async ValueTask<PlayerListDto> Handle(ListPlayersQuery query, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == query.ServerId, cancellationToken);
            if (server?.ContainerId is null) return Empty;

            try
            {
                var bytes = await docker.ExecCaptureAsync(server.ContainerId, new[] { "rcon-cli", "list" }, cancellationToken);
                var text = Encoding.UTF8.GetString(bytes);

                var m = ListRegex().Match(text);
                if (!m.Success) return Empty with { Reachable = true };

                int.TryParse(m.Groups[1].Value, out var online);
                int.TryParse(m.Groups[2].Value, out var max);
                var names = m.Groups[3].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();

                return new PlayerListDto { Online = online, Max = max, Players = names, Reachable = true };
            }
            catch
            {
                // Server offline, still booting, or RCON not up yet.
                return Empty;
            }
        }

        // Vanilla/Paper: "There are 2 of a max of 20 players online: Alice, Bob"
        [GeneratedRegex(@"There are (\d+) of a max of (\d+) players online:?\s*(.*)", RegexOptions.IgnoreCase)]
        private static partial Regex ListRegex();
    }
}
