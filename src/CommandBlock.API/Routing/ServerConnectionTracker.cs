using System.Collections.Concurrent;

namespace CommandBlock.API.Routing
{
    /// <summary>One live play connection routed through the proxy.</summary>
    public sealed record ActiveConnection(Guid Id, Guid ServerId, string RemoteAddress, DateTime OpenedAt);

    /// <summary>Tracks live player (login/play) connections routed through the proxy. It powers the
    /// idle monitor (per-server active count + last activity) and the Connections view (a snapshot of
    /// every open connection). Only real play connections are tracked - status pings don't count.</summary>
    public interface IServerConnectionTracker
    {
        /// <summary>Registers a new connection; dispose the returned handle to close it.</summary>
        IDisposable Open(Guid serverId, string remoteAddress);

        /// <summary>Current active play-connection count for a server.</summary>
        int ActiveCount(Guid serverId);

        /// <summary>UTC time a server last had activity (a connection opened or closed), if seen.</summary>
        DateTime? LastActivity(Guid serverId);

        /// <summary>Marks a server active now (e.g. when it's woken) so the idle clock resets.</summary>
        void Touch(Guid serverId);

        /// <summary>All currently open connections across every server.</summary>
        IReadOnlyList<ActiveConnection> Snapshot();
    }

    public sealed class ServerConnectionTracker(TimeProvider time) : IServerConnectionTracker
    {
        private sealed class Entry { public int Active; public DateTime LastActivity; }

        private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
        private readonly ConcurrentDictionary<Guid, ActiveConnection> _connections = new();

        public IDisposable Open(Guid serverId, string remoteAddress)
        {
            var now = time.GetUtcNow().UtcDateTime;
            var e = _entries.GetOrAdd(serverId, _ => new Entry());
            lock (e) { e.Active++; e.LastActivity = now; }

            var conn = new ActiveConnection(Guid.NewGuid(), serverId, remoteAddress, now);
            _connections[conn.Id] = conn;
            return new Handle(this, conn);
        }

        private void Close(ActiveConnection conn)
        {
            _connections.TryRemove(conn.Id, out _);
            if (_entries.TryGetValue(conn.ServerId, out var e))
                lock (e) { if (e.Active > 0) e.Active--; e.LastActivity = time.GetUtcNow().UtcDateTime; }
        }

        public int ActiveCount(Guid serverId) => _entries.TryGetValue(serverId, out var e) ? Volatile.Read(ref e.Active) : 0;

        public DateTime? LastActivity(Guid serverId) => _entries.TryGetValue(serverId, out var e) ? e.LastActivity : null;

        public void Touch(Guid serverId)
        {
            var e = _entries.GetOrAdd(serverId, _ => new Entry());
            lock (e) { e.LastActivity = time.GetUtcNow().UtcDateTime; }
        }

        public IReadOnlyList<ActiveConnection> Snapshot() => _connections.Values.ToList();

        private sealed class Handle(ServerConnectionTracker owner, ActiveConnection conn) : IDisposable
        {
            private int _disposed;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Close(conn);
            }
        }
    }
}
