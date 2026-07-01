using System.Collections.Concurrent;

namespace CommandBlock.API.Routing
{
    /// <summary>Tracks live player (login/play) connections per server so the idle monitor can sleep
    /// servers with nobody on them. Only real play connections are counted - status pings don't.</summary>
    public interface IServerConnectionTracker
    {
        void Opened(Guid serverId);
        void Closed(Guid serverId);

        /// <summary>Current active play-connection count for a server.</summary>
        int ActiveCount(Guid serverId);

        /// <summary>UTC time a server last had activity (a connection opened or closed), if seen.</summary>
        DateTime? LastActivity(Guid serverId);

        /// <summary>Marks a server active now (e.g. when it's woken) so the idle clock resets.</summary>
        void Touch(Guid serverId);
    }

    public sealed class ServerConnectionTracker(TimeProvider time) : IServerConnectionTracker
    {
        private sealed class Entry { public int Active; public DateTime LastActivity; }

        private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

        public void Opened(Guid serverId)
        {
            var e = _entries.GetOrAdd(serverId, _ => new Entry());
            lock (e) { e.Active++; e.LastActivity = time.GetUtcNow().UtcDateTime; }
        }

        public void Closed(Guid serverId)
        {
            var e = _entries.GetOrAdd(serverId, _ => new Entry());
            lock (e) { if (e.Active > 0) e.Active--; e.LastActivity = time.GetUtcNow().UtcDateTime; }
        }

        public int ActiveCount(Guid serverId) => _entries.TryGetValue(serverId, out var e) ? Volatile.Read(ref e.Active) : 0;

        public DateTime? LastActivity(Guid serverId) => _entries.TryGetValue(serverId, out var e) ? e.LastActivity : null;

        public void Touch(Guid serverId)
        {
            var e = _entries.GetOrAdd(serverId, _ => new Entry());
            lock (e) { e.LastActivity = time.GetUtcNow().UtcDateTime; }
        }
    }
}
