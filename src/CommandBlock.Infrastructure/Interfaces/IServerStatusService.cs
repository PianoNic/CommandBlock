namespace CommandBlock.Infrastructure.Interfaces
{
    /// <summary>Live status of one server: its container state (with "starting" while the container
    /// runs but the server hasn't accepted RCON yet) and current player count.</summary>
    /// <summary><paramref name="RunningVersion"/> and <paramref name="Motd"/> are what the server itself
    /// reports on the list ping, which is the actual running build ("Paper 26.1.2") rather than the version
    /// it was configured with. Both ride along in the same mc-monitor output as the player counts, so they
    /// cost nothing extra.</summary>
    public sealed record ServerStatus(
        Guid Id, string? State, int? PlayersOnline, int? PlayersMax, long? MemoryBytes,
        string? RunningVersion = null, string? Motd = null);

    /// <summary>Computes live statuses for all servers from Docker + RCON. Shared by the servers list
    /// query and the status SignalR stream so both agree.</summary>
    public interface IServerStatusService
    {
        Task<IReadOnlyList<ServerStatus>> GetAllAsync(CancellationToken cancellationToken = default);
    }
}
