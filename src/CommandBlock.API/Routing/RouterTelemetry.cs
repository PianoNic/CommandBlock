namespace CommandBlock.API.Routing
{
    /// <summary>A connection that has finished, kept for the Connections view's history and stats.</summary>
    public sealed record ConnectionRecord(Guid ServerId, string RemoteAddress, DateTime OpenedAt, DateTime ClosedAt)
    {
        public double DurationSeconds => (ClosedAt - OpenedAt).TotalSeconds;
    }

    /// <summary>One wake-on-connect: how long the container took from start to accepting players.</summary>
    public sealed record WakeRecord(Guid ServerId, DateTime StartedAt, double SecondsToReady, bool Succeeded);

    public sealed record TelemetrySnapshot(
        DateTime SinceUtc,
        int PeakConcurrent,
        IReadOnlyList<ConnectionRecord> Connections,
        IReadOnlyList<WakeRecord> Wakes,
        IReadOnlyDictionary<string, int> Rejections);

    /// <summary>Rolling in-memory history of what the router did: finished connections, wake timings and
    /// turned-away joins. Deliberately not persisted - these are operational vitals, not records worth a
    /// table and a migration, and the view labels everything "since <see cref="TelemetrySnapshot.SinceUtc"/>"
    /// so a restart reading as zero is honest rather than misleading. Buffers are capped, so memory is
    /// bounded no matter how long the process runs.</summary>
    public interface IRouterTelemetry
    {
        void RecordConnection(Guid serverId, string remoteAddress, DateTime openedAt, DateTime closedAt);

        /// <summary>Reports the current total across all servers so the peak can be tracked.</summary>
        void RecordConcurrent(int active);

        void RecordWake(Guid serverId, double secondsToReady, bool succeeded);

        /// <summary>Counts a join the router turned away, keyed by why.</summary>
        void RecordRejection(string reason);

        TelemetrySnapshot Snapshot();
    }

    public sealed class RouterTelemetry(TimeProvider time) : IRouterTelemetry
    {
        // A day of joins on a home server is far below either cap; the oldest entries fall off after that.
        private const int MaxConnections = 500;
        private const int MaxWakes = 200;

        private readonly DateTime _since = time.GetUtcNow().UtcDateTime;
        private readonly Lock _gate = new();
        private readonly Queue<ConnectionRecord> _connections = new();
        private readonly Queue<WakeRecord> _wakes = new();
        private readonly Dictionary<string, int> _rejections = [];
        private int _peakConcurrent;

        public void RecordConnection(Guid serverId, string remoteAddress, DateTime openedAt, DateTime closedAt)
        {
            lock (_gate)
            {
                _connections.Enqueue(new ConnectionRecord(serverId, remoteAddress, openedAt, closedAt));
                while (_connections.Count > MaxConnections) _connections.Dequeue();
            }
        }

        public void RecordConcurrent(int active)
        {
            lock (_gate) { if (active > _peakConcurrent) _peakConcurrent = active; }
        }

        public void RecordWake(Guid serverId, double secondsToReady, bool succeeded)
        {
            lock (_gate)
            {
                _wakes.Enqueue(new WakeRecord(serverId, time.GetUtcNow().UtcDateTime, secondsToReady, succeeded));
                while (_wakes.Count > MaxWakes) _wakes.Dequeue();
            }
        }

        public void RecordRejection(string reason)
        {
            lock (_gate) { _rejections[reason] = _rejections.GetValueOrDefault(reason) + 1; }
        }

        public TelemetrySnapshot Snapshot()
        {
            lock (_gate)
            {
                return new TelemetrySnapshot(_since, _peakConcurrent, [.. _connections], [.. _wakes],
                    new Dictionary<string, int>(_rejections));
            }
        }
    }
}
