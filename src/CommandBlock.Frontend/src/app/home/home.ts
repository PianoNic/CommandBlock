import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideServer, lucidePlay, lucideHardDrive, lucideActivity, lucideUsers } from '@ng-icons/lucide';
import { simpleModrinth, simpleCurseforge } from '@ng-icons/simple-icons';
import { PLATFORM_ICONS, platformIcon, platformLabel } from '../shared/icons/platform-icons';
import { ServerStatusStream } from '../shared/services/server-status.stream';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ServerService } from '../api/api/server.service';
import { ActivityService } from '../api/api/activity.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';
import { ActivityEntryDto } from '../api/model/activityEntryDto';
import { ServerCreateDialog } from '../servers/server-create-dialog';

@Component({
  selector: 'app-home',
  imports: [RouterLink, NgIcon, HlmButtonImports, ContentHeader],
  providers: [
    provideIcons({
      lucidePlus, lucideServer, lucidePlay, lucideHardDrive, lucideActivity, lucideUsers,
      simpleModrinth, simpleCurseforge, ...PLATFORM_ICONS,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './home.html',
})
export class Home {
  private readonly api = inject(ServerService);
  private readonly activityApi = inject(ActivityService);
  private readonly dialog = inject(HlmDialogService);
  private readonly statusStream = inject(ServerStatusStream);
  private readonly statuses = this.statusStream.statuses;

  protected readonly servers = signal<ReadonlyArray<ServerInstanceDto>>([]);
  protected readonly activity = signal<ReadonlyArray<ActivityEntryDto>>([]);

  protected readonly total = computed(() => this.servers().length);
  protected readonly running = computed(
    () => this.servers().filter((s) => (this.statuses()[s.id]?.state ?? s.state) === 'running').length,
  );
  protected readonly memory = computed(() => {
    const mb = this.servers().reduce((sum, s) => sum + parseMemory(s.memory), 0);
    return mb >= 1024 ? `${(mb / 1024).toFixed(mb % 1024 === 0 ? 0 : 1)} GB` : `${mb} MB`;
  });
  protected readonly players = computed(() =>
    this.servers().reduce((sum, s) => {
      const live = this.statuses()[s.id];
      const online = live ? live.playersOnline : (s.playersOnline == null ? null : Number(s.playersOnline as unknown as number));
      return sum + (online ?? 0);
    }, 0),
  );
  protected readonly byType = computed(() => {
    const counts = new Map<string, number>();
    for (const s of this.servers()) counts.set(s.serverType, (counts.get(s.serverType) ?? 0) + 1);
    return [...counts.entries()].map(([type, count]) => ({ type, count })).sort((a, b) => b.count - a.count);
  });

  protected icon(serverType: string): string {
    return platformIcon(serverType);
  }

  protected label(serverType: string): string {
    return platformLabel(serverType);
  }

  constructor() {
    this.statusStream.start();
    this.load();

    // Live membership: when the status stream reports a server we don't have (created) or drops one
    // we do have (deleted), re-fetch so the counts and "By type" breakdown stay current.
    effect(() => {
      const liveIds = Object.keys(this.statuses());
      if (liveIds.length === 0) return;
      const current = new Set(this.servers().map((s) => s.id));
      const liveSet = new Set(liveIds);
      const added = liveIds.some((id) => !current.has(id));
      const removed = this.servers().some((s) => !liveSet.has(s.id));
      if (added || removed) this.load();
    });
  }

  protected load(): void {
    this.api.apiServerGet().subscribe((rows) => this.servers.set(rows));
    this.activityApi.apiActivityGet().subscribe((rows) => this.activity.set(rows.slice(0, 8)));
  }

  protected createServer(): void {
    this.dialog.open(ServerCreateDialog, {
      context: { onCreated: () => this.load() },
      contentClass: 'sm:max-w-[560px]',
    });
  }
}

function parseMemory(mem: string): number {
  const m = /^\s*(\d+(?:\.\d+)?)\s*([gmk]?)/i.exec(mem ?? '');
  if (!m) return 0;
  const n = parseFloat(m[1]);
  switch (m[2].toLowerCase()) {
    case 'g': return Math.round(n * 1024);
    case 'k': return Math.round(n / 1024);
    case 'm': default: return Math.round(n);
  }
}
