import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePlus, lucidePlay, lucideSquare, lucideRotateCcw, lucideActivity, lucideUsers,
  lucideHourglass, lucideTriangleAlert, lucideMoon, lucideCircleCheck,
} from '@ng-icons/lucide';
import { PLATFORM_ICONS, platformIcon, platformLabel } from '../shared/icons/platform-icons';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ActivityService } from '../api/api/activity.service';
import { HostService } from '../api/api/host.service';
import { ActivityEntryDto } from '../api/model/activityEntryDto';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';
import { ServerCreateDialog } from '../servers/server-create-dialog';
import { ServersStore } from '../servers/servers.store';
import { environment } from '../shared/environments/environment';

/// The dashboard is a control surface, not a report: every server is actionable from here, and only
/// things that need a decision are promoted to the top. Purely descriptive numbers (server count,
/// "memory allocated", the type breakdown) were dropped - none of them ever changed what anyone did.
@Component({
  selector: 'app-home',
  imports: [RouterLink, NgIcon, HlmButtonImports, ContentHeader],
  providers: [
    provideIcons({
      lucidePlus, lucidePlay, lucideSquare, lucideRotateCcw, lucideActivity, lucideUsers,
      lucideHourglass, lucideTriangleAlert, lucideMoon, lucideCircleCheck,
      ...PLATFORM_ICONS,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './home.html',
})
export class Home {
  private readonly activityApi = inject(ActivityService);
  private readonly hostApi = inject(HostService);
  private readonly dialog = inject(HlmDialogService);
  private readonly store = inject(ServersStore);

  protected readonly servers = this.store.servers;
  protected readonly running = this.store.running;
  protected readonly total = this.store.total;
  protected readonly players = this.store.playersOnline;

  protected readonly activity = signal<ReadonlyArray<ActivityEntryDto>>([]);
  private readonly hostTotal = signal(0);
  private readonly hostAvailable = signal(0);

  /// Symptom-level only. When it's empty the band disappears entirely rather than sitting there
  /// permanently lit - a badge that never clears stops being read.
  protected readonly attention = computed(() =>
    this.servers().filter((s) => this.stateOf(s) === 'crashed'),
  );

  protected readonly runningServers = computed(() =>
    this.servers().filter((s) => this.stateOf(s) === 'running'),
  );

  protected readonly sleepingCount = computed(() =>
    this.servers().filter((s) => this.stateOf(s) === 'sleeping').length,
  );

  /// Host memory as headroom, which answers "can I start another server?" - unlike a sum of configured
  /// heaps, which never moves on its own and prompts no decision.
  protected readonly hostUsedPercent = computed(() => {
    const total = this.hostTotal();
    if (total <= 0) return 0;
    return Math.min(100, Math.round(((total - this.hostAvailable()) / total) * 100));
  });
  protected readonly hostUsedLabel = computed(() =>
    this.hostTotal() > 0 ? `${gb(this.hostTotal() - this.hostAvailable())} / ${gb(this.hostTotal())} GB used` : '-',
  );
  protected readonly hostBarClass = computed(() => {
    const p = this.hostUsedPercent();
    if (p >= 90) return 'bg-destructive';
    if (p >= 70) return 'bg-yellow-500';
    return 'bg-primary';
  });

  constructor() {
    this.store.load();
    this.loadActivity();
    this.loadHost();
    const timer = setInterval(() => this.loadHost(), 15000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected stateOf(s: ServerInstanceDto): string | null {
    return this.store.statuses()[s.id]?.state ?? s.state ?? null;
  }

  protected playersOf(s: ServerInstanceDto): string {
    const live = this.store.statuses()[s.id];
    const online = live ? live.playersOnline : s.playersOnline;
    const max = live ? live.playersMax : s.playersMax;
    return online === null || online === undefined ? '-' : `${online}/${max ?? '?'}`;
  }

  protected memoryPercent(s: ServerInstanceDto): number {
    const used = Number(this.store.statuses()[s.id]?.memoryBytes ?? s.memoryBytes ?? 0);
    const limit = parseMemoryBytes(s.memory);
    if (!used || !limit) return 0;
    return Math.min(100, Math.round((used / limit) * 100));
  }

  /// Always "used / cap" in matching units - "0 GB / 2 GB" when idle rather than collapsing to the cap.
  protected memoryLabel(s: ServerInstanceDto): string {
    const used = Number(this.store.statuses()[s.id]?.memoryBytes ?? s.memoryBytes ?? 0);
    const cap = parseMemoryBytes(s.memory);
    return `${gb(used)} GB / ${cap > 0 ? gb(cap) : '?'} GB`;
  }

  /// Warn before the wall, not at it - a server degrades well below 100% of its heap.
  protected memoryBarClass(s: ServerInstanceDto): string {
    const p = this.memoryPercent(s);
    if (p >= 90) return 'bg-destructive';
    if (p >= 70) return 'bg-yellow-500';
    return 'bg-primary';
  }

  protected stripeClass(s: ServerInstanceDto): string {
    switch (this.stateOf(s)) {
      case 'running': return 'bg-primary';
      case 'starting': return 'bg-yellow-500';
      case 'crashed': return 'bg-destructive';
      case 'sleeping': return 'bg-sky-500';
      default: return 'bg-muted-foreground/30';
    }
  }

  protected stateLabel(s: ServerInstanceDto): string {
    switch (this.stateOf(s)) {
      case 'running': return 'Running';
      case 'starting': return 'Starting…';
      case 'crashed': return 'Exited unexpectedly';
      case 'sleeping': return 'Asleep - wakes on join';
      case 'exited': return 'Stopped';
      default: return 'Unknown';
    }
  }

  protected stateTextClass(s: ServerInstanceDto): string {
    switch (this.stateOf(s)) {
      case 'running': return 'text-primary';
      case 'crashed': return 'text-destructive';
      case 'sleeping': return 'text-sky-500';
      default: return 'text-muted-foreground';
    }
  }

  protected isRunning(s: ServerInstanceDto): boolean { return this.stateOf(s) === 'running'; }
  protected isSleeping(s: ServerInstanceDto): boolean { return this.stateOf(s) === 'sleeping'; }
  protected isBusy(s: ServerInstanceDto): boolean {
    return this.store.isBusy(s.id) || this.stateOf(s) === 'starting';
  }

  protected start(s: ServerInstanceDto): void { this.store.start(s.id); }
  protected stop(s: ServerInstanceDto): void { this.store.stop(s.id); }
  protected restart(s: ServerInstanceDto): void { this.store.restart(s.id); }

  /// Only touches servers that are actually up, so it can never boot the whole estate by accident.
  protected stopAll(): void {
    for (const s of this.runningServers()) this.store.stop(s.id);
  }

  protected icon(serverType: string): string { return platformIcon(serverType); }
  protected label(serverType: string): string { return platformLabel(serverType); }

  protected iconUrl(s: ServerInstanceDto): string {
    return `${environment.apiBaseUrl}/api/Server/${s.id}/icon`;
  }

  protected createServer(): void {
    this.dialog.open(ServerCreateDialog, {
      context: { onCreated: () => { this.store.load(); this.loadActivity(); } },
      contentClass: 'sm:max-w-[560px]',
    });
  }

  private loadActivity(): void {
    this.activityApi.apiActivityGet().subscribe((rows) => this.activity.set(rows.slice(0, 6)));
  }

  private loadHost(): void {
    this.hostApi.apiHostResourcesGet().subscribe({
      next: (r) => {
        this.hostTotal.set(Number(r.totalMemoryBytes ?? 0));
        this.hostAvailable.set(Number(r.availableMemoryBytes ?? 0));
      },
      error: () => { this.hostTotal.set(0); this.hostAvailable.set(0); },
    });
  }
}

/// At most one decimal, dropping a trailing ".0" so caps read "2 GB" rather than "2.0 GB".
function gb(bytes: number): string {
  return String(Math.round((bytes / 1024 ** 3) * 10) / 10);
}

/// "4G" / "2048M" -> bytes, so usage can be shown against the configured ceiling.
function parseMemoryBytes(memory: string | null | undefined): number {
  if (!memory) return 0;
  const text = memory.trim();
  const unit = text.slice(-1).toUpperCase();
  const value = Number(unit === 'G' || unit === 'M' || unit === 'K' ? text.slice(0, -1) : text);
  if (!Number.isFinite(value)) return 0;
  if (unit === 'G') return value * 1024 ** 3;
  if (unit === 'M') return value * 1024 ** 2;
  if (unit === 'K') return value * 1024;
  return value;
}
