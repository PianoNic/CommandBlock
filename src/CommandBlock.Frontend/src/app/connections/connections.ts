import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { interval } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideActivity,
  lucideGlobe,
  lucideHistory,
  lucideNetwork,
  lucideShieldAlert,
  lucideZap,
} from '@ng-icons/lucide';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ConnectionsService } from '../api/api/connections.service';
import { ConnectionDto } from '../api/model/connectionDto';
import { ConnectionStatsDto } from '../api/model/connectionStatsDto';

/// Why the router turned a join away, in the operator's words rather than the wire's.
const REJECTION_LABELS: Record<string, string> = {
  'unknown-host': 'Address not routed here',
  'server-offline': 'Server offline, wake disabled',
  'asked-to-reconnect': 'Asked to reconnect',
  'wake-timed-out': 'Gave up waiting for boot',
};

@Component({
  selector: 'app-connections',
  imports: [ContentHeader, NgIcon, DatePipe],
  providers: [
    provideIcons({ lucideNetwork, lucideGlobe, lucideZap, lucideActivity, lucideHistory, lucideShieldAlert }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />

    <section class="flex min-h-0 flex-1 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <div>
          <h2 class="text-sm font-medium">Connections</h2>
          <p class="text-muted-foreground text-xs">Everything routed through the proxy, live.</p>
        </div>
        @if (stats(); as s) {
          <span class="text-muted-foreground shrink-0 text-xs">Since {{ s.sinceUtc | date: 'short' }}</span>
        }
      </header>

      <div class="grid min-h-0 flex-1 grid-cols-1 overflow-auto lg:grid-cols-2 lg:overflow-hidden">
        <!-- Left: who is connected right now, and who just was. -->
        <div class="flex min-h-0 flex-col lg:overflow-auto">
          <div class="grid grid-cols-2 gap-px border-b bg-border sm:grid-cols-4">
            @for (kpi of kpis(); track kpi.label) {
              <div class="bg-background px-4 py-3">
                <p class="text-muted-foreground text-xs">{{ kpi.label }}</p>
                <p class="mt-0.5 text-xl font-semibold tabular-nums">{{ kpi.value }}</p>
              </div>
            }
          </div>

          <h3 class="text-muted-foreground flex items-center gap-1.5 px-4 pt-4 pb-2 text-xs font-medium uppercase tracking-wide">
            <ng-icon name="lucideActivity" size="13" />Live now
          </h3>
          @if (connections().length === 0) {
            <p class="text-muted-foreground px-4 pb-4 text-xs">
              Nobody is connected. Players joining through the router appear here in real time.
            </p>
          } @else {
            <ul class="divide-border divide-y border-y">
              @for (c of connections(); track c.serverId + '|' + c.remoteAddress + '|' + c.openedAt) {
                <li class="flex items-center gap-3 px-4 py-2 text-sm">
                  <span class="size-1.5 shrink-0 rounded-full bg-emerald-500"></span>
                  <span class="truncate font-medium">{{ c.serverName }}</span>
                  <span class="text-muted-foreground inline-flex min-w-0 items-center gap-1 font-mono text-xs">
                    <ng-icon name="lucideGlobe" size="12" class="shrink-0 opacity-60" />
                    <span class="truncate">{{ c.hostname }}</span>
                  </span>
                  <div class="flex-1"></div>
                  <span class="shrink-0 font-mono text-xs">{{ c.remoteAddress }}</span>
                  <span class="text-muted-foreground shrink-0 text-xs tabular-nums">{{ since(c.openedAt) }}</span>
                </li>
              }
            </ul>
          }

          <h3 class="text-muted-foreground flex items-center gap-1.5 px-4 pt-4 pb-2 text-xs font-medium uppercase tracking-wide">
            <ng-icon name="lucideHistory" size="13" />Recent sessions
          </h3>
          @if (recent().length === 0) {
            <p class="text-muted-foreground px-4 pb-4 text-xs">No finished sessions yet.</p>
          } @else {
            <ul class="divide-border divide-y border-t">
              @for (r of recent(); track r.openedAt + r.remoteAddress) {
                <li class="text-muted-foreground flex items-center gap-3 px-4 py-1.5 text-xs">
                  <span class="text-foreground truncate">{{ r.serverName }}</span>
                  <span class="truncate font-mono">{{ r.remoteAddress }}</span>
                  <div class="flex-1"></div>
                  <span class="shrink-0 tabular-nums">{{ duration(r.durationSeconds) }}</span>
                  <span class="w-24 shrink-0 text-right tabular-nums">{{ r.openedAt | date: 'shortTime' }}</span>
                </li>
              }
            </ul>
          }
        </div>

        <!-- Right: how the router is behaving over time. -->
        <div class="flex min-h-0 flex-col border-t lg:border-t-0 lg:border-l lg:overflow-auto">
          <section class="border-b p-4">
            <h3 class="text-muted-foreground mb-3 text-xs font-medium uppercase tracking-wide">Joins per hour (24h)</h3>
            <div class="flex h-24 items-end gap-0.5">
              @for (b of traffic(); track b.hourUtc) {
                <div
                  class="min-h-px flex-1 rounded-sm transition-colors"
                  [class]="b.connections > 0 ? 'bg-primary/70 hover:bg-primary' : 'bg-muted'"
                  [style.height.%]="barHeight(b.connections)"
                  [title]="b.connections + ' joins at ' + (b.hourUtc | date: 'shortTime')"
                ></div>
              }
            </div>
            <div class="text-muted-foreground mt-1.5 flex justify-between text-[10px]">
              <span>24h ago</span>
              <span>peak {{ peakHour() }}/h</span>
              <span>now</span>
            </div>
          </section>

          <section class="border-b p-4">
            <h3 class="text-muted-foreground mb-3 text-xs font-medium uppercase tracking-wide">Traffic by server</h3>
            @if (byServer().length === 0) {
              <p class="text-muted-foreground text-xs">No connections recorded yet.</p>
            } @else {
              <ul class="flex flex-col gap-2">
                @for (s of byServer(); track s.serverId) {
                  <li class="flex flex-col gap-1">
                    <div class="flex items-baseline gap-2 text-xs">
                      @if (s.activeNow > 0) {
                        <span class="size-1.5 shrink-0 rounded-full bg-emerald-500"></span>
                      }
                      <span class="truncate font-medium">{{ s.serverName }}</span>
                      <div class="flex-1"></div>
                      @if (s.activeNow > 0) {
                        <span class="text-emerald-600 tabular-nums dark:text-emerald-500">{{ s.activeNow }} on now</span>
                      }
                      <span class="text-muted-foreground tabular-nums">{{ s.connections }}</span>
                    </div>
                    <div class="bg-muted h-1.5 overflow-hidden rounded-full">
                      <div class="bg-primary h-full rounded-full" [style.width.%]="serverShare(s.connections)"></div>
                    </div>
                  </li>
                }
              </ul>
            }
          </section>

          <section class="border-b p-4">
            <h3 class="text-muted-foreground mb-3 flex items-center gap-1.5 text-xs font-medium uppercase tracking-wide">
              <ng-icon name="lucideZap" size="13" />Wake on join
            </h3>
            @if (stats(); as s) {
              @if (s.wakes.total === 0) {
                <p class="text-muted-foreground text-xs">No servers have been woken yet.</p>
              } @else {
                <div class="grid grid-cols-2 gap-3 sm:grid-cols-4">
                  <div>
                    <p class="text-muted-foreground text-xs">Wakes</p>
                    <p class="text-lg font-semibold tabular-nums">{{ s.wakes.total }}</p>
                  </div>
                  <div>
                    <p class="text-muted-foreground text-xs">Typical</p>
                    <p class="text-lg font-semibold tabular-nums">{{ duration(s.wakes.medianSeconds) }}</p>
                  </div>
                  <div>
                    <p class="text-muted-foreground text-xs">Slow (p95)</p>
                    <p class="text-lg font-semibold tabular-nums">{{ duration(s.wakes.p95Seconds) }}</p>
                  </div>
                  <div>
                    <p class="text-muted-foreground text-xs">Failed</p>
                    <p
                      class="text-lg font-semibold tabular-nums"
                      [class.text-amber-500]="s.wakes.failed > 0"
                    >
                      {{ s.wakes.failed }}
                    </p>
                  </div>
                </div>
                <p class="text-muted-foreground mt-3 text-xs">
                  How long a sleeping server takes to accept players. Players are held through this wait, so keep
                  the hold window above the p95 for joins to land automatically.
                </p>
              }
            }
          </section>

          <section class="p-4">
            <h3 class="text-muted-foreground mb-3 flex items-center gap-1.5 text-xs font-medium uppercase tracking-wide">
              <ng-icon name="lucideShieldAlert" size="13" />Turned away
            </h3>
            @if (rejections().length === 0) {
              <p class="text-muted-foreground text-xs">No joins were turned away.</p>
            } @else {
              <ul class="flex flex-col gap-1.5">
                @for (r of rejections(); track r.reason) {
                  <li class="flex items-center justify-between gap-2 text-xs">
                    <span>{{ label(r.reason) }}</span>
                    <span class="text-muted-foreground tabular-nums">{{ r.count }}</span>
                  </li>
                }
              </ul>
            }
          </section>
        </div>
      </div>
    </section>
  `,
})
export class Connections {
  private readonly api = inject(ConnectionsService);

  protected readonly connections = signal<ReadonlyArray<ConnectionDto>>([]);
  protected readonly stats = signal<ConnectionStatsDto | null>(null);

  /// Re-read on every poll so the "5m ago" column advances rather than freezing at load time.
  private readonly now = signal(Date.now());

  protected readonly traffic = computed(() => this.stats()?.traffic ?? []);
  protected readonly byServer = computed(() => this.stats()?.byServer ?? []);
  protected readonly recent = computed(() => this.stats()?.recent ?? []);
  protected readonly rejections = computed(() => this.stats()?.rejections ?? []);
  protected readonly peakHour = computed(() => Math.max(0, ...this.traffic().map((b) => b.connections)));

  protected readonly kpis = computed(() => {
    const s = this.stats();
    return [
      { label: 'Connected now', value: String(s?.activeNow ?? this.connections().length) },
      { label: 'Peak at once', value: String(s?.peakConcurrent ?? 0) },
      { label: 'Unique players', value: String(s?.uniqueAddresses ?? 0) },
      { label: 'Typical session', value: this.duration(s?.medianSessionSeconds ?? null) },
    ];
  });

  constructor() {
    this.load();
    // Connections open and close as players come and go; a few seconds is responsive without
    // hammering the API, and both endpoints are cheap in-memory reads.
    interval(4000)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.load());
  }

  protected load(): void {
    this.now.set(Date.now());
    this.api.apiConnectionsGet().subscribe({ next: (rows) => this.connections.set(rows) });
    this.api.apiConnectionsStatsGet().subscribe({ next: (s) => this.stats.set(s) });
  }

  protected label(reason: string): string {
    return REJECTION_LABELS[reason] ?? reason;
  }

  /// Bars are scaled against the busiest hour so a quiet day still shows shape rather than a flat line.
  /// Empty hours keep a muted stub so the row reads as a baseline instead of missing bars.
  protected barHeight(count: number): number {
    const peak = this.peakHour();
    if (count === 0) return 4;
    return peak === 0 ? 0 : Math.max(8, (count / peak) * 100);
  }

  protected serverShare(count: number): number {
    const top = Math.max(...this.byServer().map((s) => s.connections), 1);
    return Math.max(2, (count / top) * 100);
  }

  protected since(openedAt: string): string {
    return this.duration((this.now() - new Date(openedAt).getTime()) / 1000);
  }

  protected duration(seconds: number | null | undefined): string {
    if (seconds === null || seconds === undefined) return '-';
    if (seconds < 60) return `${Math.round(seconds)}s`;
    if (seconds < 3600) return `${Math.floor(seconds / 60)}m ${Math.round(seconds % 60)}s`;
    return `${Math.floor(seconds / 3600)}h ${Math.round((seconds % 3600) / 60)}m`;
  }
}
