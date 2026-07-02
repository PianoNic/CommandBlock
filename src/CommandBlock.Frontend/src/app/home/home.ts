import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideServer, lucidePlay, lucideHardDrive, lucideActivity, lucideUsers } from '@ng-icons/lucide';
import { PLATFORM_ICONS, platformIcon, platformLabel } from '../shared/icons/platform-icons';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ActivityService } from '../api/api/activity.service';
import { ActivityEntryDto } from '../api/model/activityEntryDto';
import { ServerCreateDialog } from '../servers/server-create-dialog';
import { ServersStore } from '../servers/servers.store';

@Component({
  selector: 'app-home',
  imports: [RouterLink, NgIcon, HlmButtonImports, ContentHeader],
  providers: [
    provideIcons({
      lucidePlus, lucideServer, lucidePlay, lucideHardDrive, lucideActivity, lucideUsers,
      ...PLATFORM_ICONS,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './home.html',
})
export class Home {
  private readonly activityApi = inject(ActivityService);
  private readonly dialog = inject(HlmDialogService);
  private readonly store = inject(ServersStore);

  // Server stats come from the shared store (it owns the list + live status + membership sync).
  protected readonly total = this.store.total;
  protected readonly running = this.store.running;
  protected readonly players = this.store.playersOnline;
  protected readonly memory = this.store.memoryLabel;
  protected readonly byType = this.store.byType;

  protected readonly activity = signal<ReadonlyArray<ActivityEntryDto>>([]);

  constructor() {
    this.loadActivity();
  }

  protected icon(serverType: string): string {
    return platformIcon(serverType);
  }

  protected label(serverType: string): string {
    return platformLabel(serverType);
  }

  private loadActivity(): void {
    this.activityApi.apiActivityGet().subscribe((rows) => this.activity.set(rows.slice(0, 8)));
  }

  protected createServer(): void {
    this.dialog.open(ServerCreateDialog, {
      context: { onCreated: () => { this.store.load(); this.loadActivity(); } },
      contentClass: 'sm:max-w-[560px]',
    });
  }
}
