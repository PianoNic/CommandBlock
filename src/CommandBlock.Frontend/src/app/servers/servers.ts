import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePlus,
  lucideServer,
  lucideGlobe,
  lucidePlay,
  lucideSquare,
  lucideRotateCcw,
  lucideTrash2,
  lucideArchive,
  lucideUsers,
  lucideLoaderCircle,
  lucideHourglass,
  lucideEllipsisVertical,
} from '@ng-icons/lucide';
import { PLATFORM_ICONS, platformIcon, platformLabel } from '../shared/icons/platform-icons';
import { ServerStatusStream } from '../shared/services/server-status.stream';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmContextMenuImports } from '@spartan-ng/helm/context-menu';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';
import { ServerCreateDialog } from './server-create-dialog';
import { ServerBackupsDialog } from './server-backups-dialog';

@Component({
  selector: 'app-servers',
  imports: [
    ContentHeader,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmTableImports,
    HlmDropdownMenuImports,
    HlmContextMenuImports,
  ],
  providers: [
    provideIcons({
      lucidePlus,
      lucideServer,
      lucideGlobe,
      lucidePlay,
      lucideSquare,
      lucideRotateCcw,
      lucideTrash2,
      lucideArchive,
      lucideUsers,
      lucideLoaderCircle,
      lucideHourglass,
      lucideEllipsisVertical,
      ...PLATFORM_ICONS,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './servers.html',
})
export class Servers {
  private readonly api = inject(ServerService);
  private readonly dialog = inject(HlmDialogService);
  private readonly confirm = inject(ConfirmService);
  private readonly statusStream = inject(ServerStatusStream);
  private readonly router = inject(Router);
  protected readonly statuses = this.statusStream.statuses;

  protected readonly servers = signal<ReadonlyArray<ServerInstanceDto>>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  // Ids with a start/stop in flight, for instant hourglass feedback before the status tick lands.
  protected readonly busy = signal<ReadonlySet<string>>(new Set());

  constructor() {
    this.statusStream.start();
    this.load();

    // Live membership: when the status stream reports a server we don't have (created) or drops one
    // we do have (deleted) - anywhere, by anyone - re-fetch the full list. State/players patch live
    // from the stream directly; this only handles rows appearing/disappearing.
    effect(() => {
      if (!this.statusStream.received()) return; // no snapshot yet - don't churn on startup
      const liveIds = Object.keys(this.statuses());
      const current = new Set(this.servers().map((s) => s.id));
      const liveSet = new Set(liveIds);
      const added = liveIds.some((id) => !current.has(id));
      const removed = this.servers().some((s) => !liveSet.has(s.id));
      if ((added || removed) && !this.loading()) this.load();
    });

    // Clear the in-flight flag once the server settles into a terminal state.
    effect(() => {
      const st = this.statuses();
      const b = this.busy();
      if (b.size === 0) return;
      const next = new Set(b);
      let changed = false;
      for (const id of b) {
        const state = st[id]?.state;
        // Settled (running/exited/stopped) or the server no longer exists -> drop the flag.
        if (state === 'running' || state === 'exited' || state === 'stopped' || !(id in st)) {
          next.delete(id);
          changed = true;
        }
      }
      if (changed) this.busy.set(next);
    });
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.apiServerGet().subscribe({
      next: (rows) => {
        this.servers.set(rows);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load servers.');
        this.loading.set(false);
      },
    });
  }

  protected createServer(): void {
    this.dialog.open(ServerCreateDialog, {
      context: { onCreated: () => this.load() },
      contentClass: 'sm:max-w-[560px]',
    });
  }

  protected openDetail(s: ServerInstanceDto): void {
    this.router.navigate(['/servers', s.id]);
  }

  protected openBackups(s: ServerInstanceDto): void {
    this.dialog.open(ServerBackupsDialog, {
      context: { serverId: s.id, serverName: s.displayName },
      contentClass: 'sm:max-w-[640px]',
    });
  }

  protected start(s: ServerInstanceDto): void {
    this.mark(s.id);
    this.api.apiServerIdStartPost(s.id).subscribe({ next: () => this.load(), error: () => this.unmark(s.id) });
  }

  protected async stop(s: ServerInstanceDto): Promise<void> {
    const ok = await this.confirm.open({
      title: `Stop ${s.displayName}?`,
      message: 'The container stops and players are disconnected. The world is preserved.',
      confirmLabel: 'Stop',
      destructive: true,
    });
    if (!ok) return;
    this.mark(s.id);
    this.api.apiServerIdStopPost(s.id).subscribe({ next: () => this.load(), error: () => this.unmark(s.id) });
  }

  protected restart(s: ServerInstanceDto): void {
    this.mark(s.id);
    this.api.apiServerIdRestartPost(s.id).subscribe({ next: () => this.load(), error: () => this.unmark(s.id) });
  }

  private mark(id: string): void { this.busy.set(new Set(this.busy()).add(id)); }
  private unmark(id: string): void { const n = new Set(this.busy()); n.delete(id); this.busy.set(n); }

  /// True while the server is booting/transitioning or an action is in flight - the start/stop
  /// control shows an hourglass and is disabled. Covers the whole boot: created -> starting -> running.
  protected transitioning(s: ServerInstanceDto): boolean {
    const st = this.stateOf(s);
    return st === 'created' || st === 'starting' || st === 'restarting' || this.busy().has(s.id);
  }

  protected async remove(s: ServerInstanceDto): Promise<void> {
    const ok = await this.confirm.open({
      title: `Delete ${s.displayName}?`,
      message: 'This stops and removes the container and its world data. This cannot be undone (restore from a backup if you have one).',
      confirmLabel: 'Delete server',
      destructive: true,
    });
    if (!ok) return;
    this.api.apiServerIdDelete(s.id).subscribe({ next: () => this.load() });
  }

  /// Live state from the status stream, falling back to the value from the initial list load.
  protected stateOf(s: ServerInstanceDto): string | null {
    return this.statuses()[s.id]?.state ?? s.state ?? null;
  }

  protected stateVariant(state: string | null | undefined): 'default' | 'secondary' | 'outline' {
    return state === 'running' ? 'default' : state ? 'secondary' : 'outline';
  }

  protected isRunning(s: ServerInstanceDto): boolean {
    return this.stateOf(s) === 'running';
  }

  protected sourceLabel(s: ServerInstanceDto): string {
    return s.modpackRef ?? s.version ?? 'latest';
  }

  protected icon(serverType: string): string {
    return platformIcon(serverType);
  }

  protected label(serverType: string): string {
    return platformLabel(serverType);
  }

  protected players(s: ServerInstanceDto): string {
    const live = this.statuses()[s.id];
    const online = live ? live.playersOnline : (s.playersOnline == null ? null : Number(s.playersOnline as unknown as number));
    const max = live ? live.playersMax : (s.playersMax == null ? null : Number(s.playersMax as unknown as number));
    if (online == null) return '-';
    return max == null ? `${online}` : `${online}/${max}`;
  }
}
