import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { environment } from '../environments/environment';

export interface ServerStatus {
  id: string;
  state?: string | null;
  playersOnline?: number | null;
  playersMax?: number | null;
  memoryBytes?: number | null;
}

/// Shared live-status stream. Connects once to /hubs/status and keeps a map of serverId -> status
/// updated in real time, so the servers list and dashboard reflect start/stop/booting instantly.
@Injectable({ providedIn: 'root' })
export class ServerStatusStream {
  private readonly oidc = inject(OidcSecurityService);
  private connection?: HubConnection;
  private started = false;

  readonly statuses = signal<Record<string, ServerStatus>>({});
  /// True once the stream has delivered at least one snapshot. Lets consumers tell "no data yet"
  /// (startup) apart from "an empty snapshot" (all servers deleted).
  readonly received = signal(false);

  /// Idempotent: safe to call from every component that wants live status.
  start(): void {
    if (this.started) return;
    this.started = true;

    this.connection = new HubConnectionBuilder()
      .withUrl(`${environment.apiBaseUrl}/hubs/status`, {
        accessTokenFactory: () => firstValueFrom(this.oidc.getAccessToken()),
      })
      .withAutomaticReconnect()
      .build();

    this.connection.onreconnected(() => this.subscribe());
    this.connection.start().then(() => this.subscribe()).catch(() => { this.started = false; });
  }

  private subscribe(): void {
    this.connection?.stream('StreamStatus').subscribe({
      next: (snapshot: ServerStatus[]) => {
        const previous = this.statuses();
        const map: Record<string, ServerStatus> = {};
        for (const s of snapshot) map[s.id] = mergeWithLastKnown(s, previous[s.id]);
        this.statuses.set(map);
        this.received.set(true);
      },
      error: () => {},
      complete: () => {},
    });
  }
}

/// A single dropped probe (the memory read or the server ping can time out) would otherwise make
/// memory snap to 0 and player counts blank out for one tick, so the values visibly flicker. While a
/// server is still up, carry the last known reading over instead of rendering a hole. Once it stops
/// being up the nulls are real and pass straight through.
function mergeWithLastKnown(next: ServerStatus, previous: ServerStatus | undefined): ServerStatus {
  if (!previous || (next.state !== 'running' && next.state !== 'starting')) return next;
  return {
    ...next,
    memoryBytes: next.memoryBytes ?? previous.memoryBytes ?? null,
    playersOnline: next.playersOnline ?? previous.playersOnline ?? null,
    playersMax: next.playersMax ?? previous.playersMax ?? null,
  };
}
