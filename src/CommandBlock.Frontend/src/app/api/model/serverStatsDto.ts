/**
 * CommandBlock API
 *
 * Live detail-page vitals for a single server.
 */

export interface ServerStatsDto { 
    state?: string | null;
    cpuPercent?: number | null;
    memoryBytes?: number | null;
    memoryLimitBytes?: number | null;
    startedAt?: string | null;
    runningVersion?: string | null;
    motd?: string | null;
    playersOnline?: number | null;
    playersMax?: number | null;
}
