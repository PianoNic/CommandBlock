import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucidePlay,
  lucideSquare,
  lucideRotateCcw,
  lucideHourglass,
  lucideFolder,
  lucideArchive,
  lucideSettings2,
  lucideTrash2,
  lucideGlobe,
  lucideUsers,
  lucideEllipsisVertical,
} from '@ng-icons/lucide';
import { PLATFORM_ICONS, platformIcon, platformLabel } from '../shared/icons/platform-icons';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { ServerStatusStream } from '../shared/services/server-status.stream';
import { ServerConsole } from '../console/server-console';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';
import { ServerStatsDto } from '../api/model/serverStatsDto';
import { PlayerListDto } from '../api/model/playerListDto';
import { ServerBackupsDialog } from './server-backups-dialog';
import { ServerSettingsDialog } from './server-settings-dialog';
import { environment } from '../shared/environments/environment';

@Component({
  selector: 'app-server-detail',
  imports: [RouterLink, NgIcon, HlmButtonImports, HlmTooltipImports, HlmDropdownMenuImports, ContentHeader, ServerConsole],
  providers: [
    provideIcons({
      lucideArrowLeft,
      lucidePlay,
      lucideSquare,
      lucideRotateCcw,
      lucideHourglass,
      lucideFolder,
      lucideArchive,
      lucideSettings2,
      lucideTrash2,
      lucideGlobe,
      lucideUsers,
      lucideEllipsisVertical,
      ...PLATFORM_ICONS,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />

    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex flex-wrap items-center justify-between gap-2 border-b py-2">
        <div class="flex min-w-0 items-center gap-2">
          <a hlmBtn size="sm" variant="ghost" routerLink="/servers" title="Back"><ng-icon name="lucideArrowLeft" size="16" /></a>
          @if (server(); as s) {
            <img [src]="s.hasIcon ? iconUrl(s) : 'default-server-icon.png'" alt="" class="h-[22px] w-[22px] shrink-0 rounded-sm" style="image-rendering:pixelated" />
            <h2 class="truncate text-sm font-medium">{{ s.displayName }}</h2>
          } @else {
            <h2 class="text-sm font-medium">Server</h2>
          }
        </div>

        @if (server(); as s) {
          <div class="flex items-center gap-1.5">
            @if (transitioning()) {
              <button hlmBtn size="sm" type="button" disabled><ng-icon name="lucideHourglass" size="14" /> Working…</button>
            } @else if (isRunning()) {
              <button hlmBtn size="sm" type="button" (click)="stop(s)"><ng-icon name="lucideSquare" size="14" /> Stop</button>
              <button hlmBtn size="sm" variant="outline" type="button" (click)="restart(s)"><ng-icon name="lucideRotateCcw" size="14" /> Restart</button>
            } @else {
              <button hlmBtn size="sm" type="button" (click)="start(s)"><ng-icon name="lucidePlay" size="14" /> Start</button>
            }
            <!-- Everything else behind a kebab menu -->
            <button hlmBtn size="sm" variant="outline" type="button" [hlmDropdownMenuTrigger]="hdrMenu" align="end" title="More actions">
              <ng-icon name="lucideEllipsisVertical" size="16" />
            </button>
            <ng-template #hdrMenu>
              <hlm-dropdown-menu class="min-w-44">
                <a hlmDropdownMenuItem [routerLink]="['/files', s.id]"><ng-icon name="lucideFolder" size="14" /> Files</a>
                <button hlmDropdownMenuItem (click)="openBackups(s)"><ng-icon name="lucideArchive" size="14" /> Backups</button>
                <button hlmDropdownMenuItem (click)="openSettings(s)"><ng-icon name="lucideSettings2" size="14" /> Settings</button>
                <hlm-dropdown-menu-separator />
                <button hlmDropdownMenuItem (click)="remove(s)" class="text-destructive"><ng-icon name="lucideTrash2" size="14" /> Delete</button>
              </hlm-dropdown-menu>
            </ng-template>
          </div>
        }
      </header>

      @if (loading()) {
        <div class="p-4"><p class="text-muted-foreground text-sm">Loading…</p></div>
      } @else if (!server()) {
        <div class="text-muted-foreground flex flex-1 flex-col items-center justify-center gap-2 text-center">
          <p class="text-sm">Server not found.</p>
          <a hlmBtn size="sm" variant="outline" routerLink="/servers">Back to servers</a>
        </div>
      } @else if (server(); as s) {
        <div class="flex min-h-0 flex-1 flex-col">
          <!-- Vitals: what an operator checks first -->
          <div class="grid grid-cols-2 gap-x-6 gap-y-1.5 border-b px-4 py-2.5 text-xs sm:grid-cols-3 lg:grid-cols-4">
            <div class="flex items-center gap-1.5">
              <span class="text-muted-foreground">Status</span>
              <span class="font-medium" [class.text-primary]="isRunning()" [class.text-foreground]="!isRunning()">{{ state() ?? 'unknown' }}</span>
            </div>

            <div class="flex items-center gap-1.5">
              <span class="text-muted-foreground">Uptime</span>
              <span class="text-foreground font-mono">{{ uptime() }}</span>
            </div>

            <div class="flex items-center gap-1.5">
              <span class="text-muted-foreground">CPU</span>
              <span class="text-foreground font-mono">{{ cpu() }}</span>
            </div>

            <div class="flex items-center gap-1.5">
              <span class="text-muted-foreground">Memory</span>
              <span class="text-foreground font-mono">{{ memoryUsage(s) }}</span>
            </div>

            <div
              class="-mx-1 flex cursor-help items-center gap-1.5 rounded px-1 transition-colors hover:bg-secondary"
              [hlmTooltip]="playersTooltip()"
              (mouseenter)="loadPlayers()"
            >
              <span class="text-muted-foreground inline-flex items-center gap-1"><ng-icon name="lucideUsers" size="12" class="opacity-60" /> Players</span>
              <span class="text-foreground font-mono">{{ players() }}</span>
            </div>

            <div class="flex items-center gap-1.5">
              <span class="text-muted-foreground">Version</span>
              <span class="text-foreground font-mono">{{ runningVersion() || sourceLabel(s) }}</span>
            </div>

            <div class="flex items-center gap-1.5">
              <span class="text-muted-foreground inline-flex items-center gap-1"><ng-icon [name]="icon(s.serverType)" size="12" /> Type</span>
              <span class="text-foreground">{{ label(s.serverType) }}</span>
            </div>

            <div class="flex min-w-0 items-center gap-1.5">
              <span class="text-muted-foreground inline-flex items-center gap-1"><ng-icon name="lucideGlobe" size="12" class="opacity-60" /> Address</span>
              <span class="text-foreground truncate font-mono">{{ s.hostname }}</span>
            </div>

          </div>

          <!-- Console fills the rest -->
          <app-server-console [serverId]="s.id" class="min-h-0 flex-1" />
        </div>
      }
    </section>
  `,
})
export class ServerDetail {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(ServerService);
  private readonly dialog = inject(HlmDialogService);
  private readonly confirm = inject(ConfirmService);
  private readonly statusStream = inject(ServerStatusStream);
  private readonly statuses = this.statusStream.statuses;

  private readonly id = this.route.snapshot.paramMap.get('id') ?? '';
  protected readonly server = signal<ServerInstanceDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly playerList = signal<PlayerListDto | null>(null);
  protected readonly playersLoading = signal(false);

  /// Vitals (CPU/uptime/build) come from a dedicated endpoint: the CPU reading needs a ~1s sample from
  /// the daemon, so it's deliberately not part of the list/status stream.
  protected readonly stats = signal<ServerStatsDto | null>(null);
  private statsTimer: ReturnType<typeof setInterval> | null = null;

  protected readonly cpu = computed(() => {
    // The generated client models nullable numbers as a union, so coerce rather than assuming number.
    const raw = this.stats()?.cpuPercent;
    if (raw === null || raw === undefined) return '-';
    const value = Number(raw);
    return Number.isFinite(value) ? `${value.toFixed(1)} %` : '-';
  });
  protected readonly runningVersion = computed(() => this.stats()?.runningVersion ?? '');
  protected readonly motd = computed(() => this.stats()?.motd ?? '');
  protected readonly uptime = computed(() => {
    const started = this.stats()?.startedAt;
    if (!started || !this.isRunning()) return '-';
    const ms = this.now() - new Date(started).getTime();
    return ms > 0 ? formatUptime(ms) : '-';
  });
  /// Ticks so uptime counts up without re-fetching.
  private readonly now = signal(Date.now());
  // Bumped after the settings modal changes the icon, to bust the header <img> cache for the same URL.
  protected readonly iconV = signal(0);

  constructor() {
    this.statusStream.start();
    this.load();
    this.loadStats();

    // Vitals refresh on their own cadence (the CPU sample costs ~1s server-side), while the clock
    // ticks separately so uptime counts up smoothly between fetches.
    this.statsTimer = setInterval(() => this.loadStats(), 6000);
    const clock = setInterval(() => this.now.set(Date.now()), 1000);
    inject(DestroyRef).onDestroy(() => {
      if (this.statsTimer) clearInterval(this.statsTimer);
      clearInterval(clock);
    });
  }

  private loadStats(): void {
    this.api.apiServerIdStatsGet(this.id).subscribe({
      // A poll that misses the CPU sample shouldn't blank the reading - carry the last good one over,
      // otherwise the value visibly flickers between a number and "-".
      next: (s) => this.stats.update((prev) => ({ ...s, cpuPercent: s.cpuPercent ?? prev?.cpuPercent ?? null })),
      error: () => { /* keep the previous snapshot rather than emptying the whole panel */ },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.api.apiServerGet().subscribe({
      next: (rows) => {
        const found = rows.find((r) => r.id === this.id) ?? null;
        this.server.set(found);
        this.loading.set(false);
        if (found && (this.statuses()[found.id]?.state ?? found.state) === 'running') this.loadPlayers();
      },
      error: () => this.loading.set(false),
    });
  }

  // Online players shown in a hover tooltip on the header count. Fetched on demand via RCON (not
  // polled) so it doesn't spam the server console.
  protected playersTooltip(): string {
    const pl = this.playerList();
    if (!pl) return 'Hover to load players';
    if (!pl.reachable) return 'Server not reachable';
    if (pl.players.length === 0) return 'No players online';
    return pl.players.join(', ');
  }

  private lastPlayersFetch = 0;

  protected loadPlayers(): void {
    const s = this.server();
    if (!s || this.playersLoading()) return;
    // RCON is on-demand: throttle so hovering the count doesn't open a new RCON connection each time.
    const now = Date.now();
    if (now - this.lastPlayersFetch < 10_000) return;
    this.lastPlayersFetch = now;
    this.playersLoading.set(true);
    this.api.apiServerIdPlayersGet(s.id).subscribe({
      next: (pl) => { this.playerList.set(pl); this.playersLoading.set(false); },
      error: () => this.playersLoading.set(false),
    });
  }

  protected readonly state = computed<string | null>(() => {
    const s = this.server();
    if (!s) return null;
    return this.statuses()[s.id]?.state ?? s.state ?? null;
  });

  protected readonly transitioning = computed(() => {
    const st = this.state();
    return this.busy() || st === 'created' || st === 'starting' || st === 'restarting';
  });

  protected isRunning(): boolean {
    return this.state() === 'running';
  }

  protected sourceLabel(s: ServerInstanceDto): string {
    return s.modpackRef ?? s.version ?? 'latest';
  }

  /// Always "used / cap" in matching units, e.g. "1.9 GB / 2 GB" - and "0 GB / 2 GB" when the server
  /// isn't running, so the field keeps its shape instead of collapsing to just the cap.
  protected memoryUsage(s: ServerInstanceDto): string {
    const live = this.statuses()[s.id];
    const raw = live ? live.memoryBytes : (s.memoryBytes as unknown as number | null | undefined);
    const used = raw == null ? 0 : Number(raw);
    const cap = parseMemoryBytes(s.memory);
    return `${formatGb(used)} GB / ${cap > 0 ? formatGb(cap) : '?'} GB`;
  }

  protected players(): string {
    const s = this.server();
    if (!s) return '-';
    const live = this.statuses()[s.id];
    const online = live ? live.playersOnline : s.playersOnline;
    const max = live ? live.playersMax : s.playersMax;
    if (online == null) return '-';
    return max == null ? `${online}` : `${online}/${max}`;
  }

  protected icon(serverType: string): string {
    return platformIcon(serverType);
  }

  protected label(serverType: string): string {
    return platformLabel(serverType);
  }

  protected iconUrl(s: ServerInstanceDto): string {
    return `${environment.apiBaseUrl}/api/Server/${s.id}/icon?v=${this.iconV()}`;
  }

  protected start(s: ServerInstanceDto): void {
    this.busy.set(true);
    this.api.apiServerIdStartPost(s.id).subscribe({ next: () => this.busy.set(false), error: () => this.busy.set(false) });
  }

  protected async stop(s: ServerInstanceDto): Promise<void> {
    const ok = await this.confirm.open({
      title: `Stop ${s.displayName}?`,
      message: 'The container stops and players are disconnected. The world is preserved.',
      confirmLabel: 'Stop',
      destructive: true,
    });
    if (!ok) return;
    this.busy.set(true);
    this.api.apiServerIdStopPost(s.id).subscribe({ next: () => this.busy.set(false), error: () => this.busy.set(false) });
  }

  protected restart(s: ServerInstanceDto): void {
    this.busy.set(true);
    this.api.apiServerIdRestartPost(s.id).subscribe({ next: () => this.busy.set(false), error: () => this.busy.set(false) });
  }

  protected async remove(s: ServerInstanceDto): Promise<void> {
    const ok = await this.confirm.open({
      title: `Delete ${s.displayName}?`,
      message: 'This stops and removes the container and its world data. This cannot be undone (restore from a backup if you have one).',
      confirmLabel: 'Delete server',
      destructive: true,
    });
    if (!ok) return;
    this.api.apiServerIdDelete(s.id).subscribe({ next: () => this.router.navigate(['/servers']) });
  }

  protected openBackups(s: ServerInstanceDto): void {
    this.dialog.open(ServerBackupsDialog, {
      context: { serverId: s.id, serverName: s.displayName },
      contentClass: 'sm:max-w-[640px]',
    });
  }

  protected openSettings(s: ServerInstanceDto): void {
    this.dialog.open(ServerSettingsDialog, {
      context: { server: s, onSaved: () => { this.iconV.update((v) => v + 1); this.load(); } },
      contentClass: 'sm:max-w-[680px]',
    });
  }
}

function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let v = n / 1024;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i++;
  }
  return `${v.toFixed(v < 10 ? 1 : 0)} ${units[i]}`;
}

/// Compact human uptime, e.g. "3d 4h", "22h 12m", "45s".
function formatUptime(ms: number): string {
  const totalSeconds = Math.floor(ms / 1000);
  const days = Math.floor(totalSeconds / 86400);
  const hours = Math.floor((totalSeconds % 86400) / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  if (minutes > 0) return `${minutes}m`;
  return `${totalSeconds}s`;
}

/// Gigabytes with at most one decimal, dropping a trailing ".0" so caps read "2 GB" not "2.0 GB".
function formatGb(bytes: number): string {
  return String(Math.round((bytes / 1024 ** 3) * 10) / 10);
}

/// "4G" / "2048M" -> bytes, so usage can be shown against the configured cap in the same unit.
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
