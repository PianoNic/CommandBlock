using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.API.Hubs
{
    /// <summary>Pushes live server statuses (state incl. "starting", player counts) to the browser so
    /// the servers list and dashboard update in real time - no client polling.</summary>
    public class ServerStatusHub(IServiceScopeFactory scopeFactory) : Hub
    {
        public async IAsyncEnumerable<IReadOnlyList<ServerStatus>> StreamStatus([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                IReadOnlyList<ServerStatus> snapshot;
                using (var scope = scopeFactory.CreateScope())
                {
                    var status = scope.ServiceProvider.GetRequiredService<IServerStatusService>();
                    snapshot = await status.GetAllAsync(cancellationToken);
                }
                yield return snapshot;

                try { await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken); }
                catch (OperationCanceledException) { yield break; }
            }
        }
    }
}
