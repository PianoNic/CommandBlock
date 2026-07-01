namespace CommandBlock.Infrastructure.Interfaces
{
    /// <summary>Live status of one server: its container state (with "starting" while the container
    /// runs but the server hasn't accepted RCON yet) and current player count.</summary>
    public sealed record ServerStatus(Guid Id, string? State, int? PlayersOnline, int? PlayersMax);

    /// <summary>Computes live statuses for all servers from Docker + RCON. Shared by the servers list
    /// query and the status SignalR stream so both agree.</summary>
    public interface IServerStatusService
    {
        Task<IReadOnlyList<ServerStatus>> GetAllAsync(CancellationToken cancellationToken = default);
    }
}
