using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.API.Hubs
{
    /// <summary>The live server console: streams a server's container logs to the browser and runs
    /// typed commands against it over RCON (via the itzg image's <c>rcon-cli</c>).</summary>
    public class ConsoleHub(CommandBlockDbContext db, IDockerServiceResolver dockerResolver) : Hub
    {
        /// <summary>Server-to-client stream of the server's console output.</summary>
        public async IAsyncEnumerable<string> StreamLogs(Guid serverId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == serverId, cancellationToken)
                ?? throw new HubException("Server not found.");
            if (server.ContainerId is null) yield break;

            var docker = dockerResolver.Resolve(server.NodeId);
            await foreach (var chunk in docker.StreamLogsAsync(server.ContainerId, 300, cancellationToken))
                yield return chunk;
        }

        /// <summary>Runs a command via RCON and returns its output.</summary>
        public async Task<string> SendCommand(Guid serverId, string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return string.Empty;

            var server = await db.ServerInstances.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == serverId)
                ?? throw new HubException("Server not found.");
            if (server.ContainerId is null) throw new HubException("This server has no container.");

            var docker = dockerResolver.Resolve(server.NodeId);
            try
            {
                // "--" stops rcon-cli's own flag parsing so a command starting with "-" can't be
                // smuggled in as a CLI flag; it's always treated as the positional MC command.
                var output = await docker.ExecCaptureAsync(server.ContainerId, new[] { "rcon-cli", "--", command.Trim() });
                return Encoding.UTF8.GetString(output);
            }
            catch (Exception ex)
            {
                throw new HubException("Command failed: " + ex.Message);
            }
        }
    }
}
