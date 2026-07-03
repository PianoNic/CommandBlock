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
    public class ConsoleHub(CommandBlockDbContext db, IDockerService docker) : Hub
    {
        /// <summary>Server-to-client stream of the server's console output. Resilient: it re-resolves the
        /// server's current container each round and re-attaches, so one subscription survives a restart
        /// or recreate (new container) and picks up a stopped server's logs the moment it comes back
        /// online - the browser never has to re-subscribe. A new container shows its boot log; a
        /// re-attach to the same container doesn't replay.</summary>
        public async IAsyncEnumerable<string> StreamLogs(Guid serverId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            string? lastContainerId = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                var containerId = await db.ServerInstances.AsNoTracking()
                    .Where(s => s.Id == serverId)
                    .Select(s => s.ContainerId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (string.IsNullOrEmpty(containerId))
                {
                    // No container yet (never started / just deleted) - wait, then retry.
                    if (!await DelayAsync(2000, cancellationToken)) yield break;
                    continue;
                }

                var tail = containerId == lastContainerId ? 0 : 300; // a new container shows its boot log
                lastContainerId = containerId;

                var logs = docker.StreamLogsAsync(containerId, tail, cancellationToken).GetAsyncEnumerator(cancellationToken);
                try
                {
                    while (true)
                    {
                        bool has;
                        try { has = await logs.MoveNextAsync(); }
                        catch { break; } // container stopped/removed mid-stream -> fall through to re-resolve
                        if (!has) break;
                        yield return logs.Current;
                    }
                }
                finally
                {
                    await logs.DisposeAsync();
                }

                // The follow ended (container stopped or recreated). Pause, then loop to re-attach to
                // whatever container the server has now.
                if (!await DelayAsync(2000, cancellationToken)) yield break;
            }
        }

        /// <summary>Cancellable delay that returns false once the client has gone away, so the stream
        /// loop can exit cleanly instead of throwing.</summary>
        private static async Task<bool> DelayAsync(int milliseconds, CancellationToken cancellationToken)
        {
            try { await Task.Delay(milliseconds, cancellationToken); return true; }
            catch (OperationCanceledException) { return false; }
        }

        /// <summary>Runs a command via RCON and returns its output.</summary>
        public async Task<string> SendCommand(Guid serverId, string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return string.Empty;

            var server = await db.ServerInstances.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == serverId)
                ?? throw new HubException("Server not found.");
            if (server.ContainerId is null) throw new HubException("This server has no container.");

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
