import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
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
          <!-- Info: one line -->
          <div class="text-muted-foreground flex items-center gap-x-5 overflow-x-auto border-b px-4 py-2 text-xs whitespace-nowrap">
            <span class="inline-flex items-center gap-1.5">
              Status
              <span class="font-medium" [class.text-primary]="isRunning()" [class.text-foreground]="!isRunning()">{{ state() ?? 'unknown' }}</span>
            </span>
            <span class="inline-flex items-center gap-1.5">
              <ng-icon name="lucideGlobe" size="12" class="opacity-60" />
              <span class="text-foreground font-mono">{{ s.hostname }}</span>
            </span>
            <span class="inline-flex items-center gap-1.5">
              <ng-icon [name]="icon(s.serverType)" size="12" />
              <span class="text-foreground">{{ label(s.serverType) }}</span>
            </span>
            <span class="inline-flex items-center gap-1.5">Version <span class="text-foreground font-mono">{{ sourceLabel(s) }}</span></span>
            <span class="inline-flex items-center gap-1.5">Memory <span class="text-foreground font-mono">{{ memoryUsage(s) }}</span></span>
            <span
              class="inline-flex cursor-help items-center gap-1.5 -mx-1 rounded px-1 transition-colors hover:bg-secondary"
              [hlmTooltip]="playersTooltip()"
              (mouseenter)="loadPlayers()"
            >
              <ng-icon name="lucideUsers" size="12" class="opacity-60" />
              <span class="text-foreground font-mono">{{ players() }}</span>
            </span>
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
  // Bumped after the settings modal changes the icon, to bust the header <img> cache for the same URL.
  protected readonly iconV = signal(0);

  constructor() {
    this.statusStream.start();
    this.load();
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

  /// "1.2 GB / 4G" when running (live usage / configured cap), else just the configured cap.
  protected memoryUsage(s: ServerInstanceDto): string {
    const live = this.statuses()[s.id];
    const raw = live ? live.memoryBytes : (s.memoryBytes as unknown as number | null | undefined);
    const bytes = raw == null ? null : Number(raw);
    return bytes == null ? s.memory : `${formatBytes(bytes)} / ${s.memory}`;
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
