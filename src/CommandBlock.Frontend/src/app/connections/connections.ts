import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { interval } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideNetwork, lucideRefreshCw, lucideGlobe } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ConnectionsService } from '../api/api/connections.service';
import { ConnectionDto } from '../api/model/connectionDto';

@Component({
  selector: 'app-connections',
  imports: [ContentHeader, NgIcon, DatePipe, HlmButtonImports],
  providers: [provideIcons({ lucideNetwork, lucideRefreshCw, lucideGlobe })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />

    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <div>
          <h2 class="text-sm font-medium">Connections</h2>
          <p class="text-muted-foreground text-xs">Live player connections routed through the proxy (updates automatically).</p>
        </div>
      </header>

      <div class="min-h-0 flex-1 overflow-auto">
        @if (connections().length === 0) {
          <div class="text-muted-foreground flex flex-col items-center gap-2 py-12 text-center">
            <ng-icon name="lucideNetwork" size="32" class="opacity-50" />
            <p class="text-sm">No active connections.</p>
            <p class="text-xs">Players joining through the router will appear here in real time.</p>
          </div>
        } @else {
          <ul class="divide-border divide-y">
            @for (c of connections(); track c.serverId + '|' + c.remoteAddress + '|' + c.openedAt) {
              <li class="flex items-center gap-3 px-4 py-2 text-sm">
                <ng-icon name="lucideNetwork" size="14" class="text-muted-foreground shrink-0" />
                <span class="font-medium">{{ c.serverName }}</span>
                <span class="text-muted-foreground inline-flex items-center gap-1 font-mono text-xs">
                  <ng-icon name="lucideGlobe" size="12" class="opacity-60" />{{ c.hostname }}
                </span>
                <div class="flex-1"></div>
                <span class="font-mono text-xs">{{ c.remoteAddress }}</span>
                <span class="text-muted-foreground text-xs">{{ c.openedAt | date: 'medium' }}</span>
              </li>
            }
          </ul>
        }
      </div>
    </section>
  `,
})
export class Connections {
  private readonly api = inject(ConnectionsService);
  protected readonly connections = signal<ReadonlyArray<ConnectionDto>>([]);

  constructor() {
    this.load();
    // Poll a few times a minute; connections open/close as players join and leave.
    interval(3000)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.load());
  }

  protected load(): void {
    this.api.apiConnectionsGet().subscribe({ next: (rows) => this.connections.set(rows) });
  }
}
