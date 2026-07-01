import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePlus,
  lucideServer,
  lucideGlobe,
  lucidePlay,
  lucideSquare,
  lucideTrash2,
  lucideArchive,
} from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmTableImports } from '@spartan-ng/helm/table';
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
  ],
  providers: [
    provideIcons({
      lucidePlus,
      lucideServer,
      lucideGlobe,
      lucidePlay,
      lucideSquare,
      lucideTrash2,
      lucideArchive,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './servers.html',
})
export class Servers {
  private readonly api = inject(ServerService);
  private readonly dialog = inject(HlmDialogService);
  private readonly confirm = inject(ConfirmService);

  protected readonly servers = signal<ReadonlyArray<ServerInstanceDto>>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.load();
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

  protected openBackups(s: ServerInstanceDto): void {
    this.dialog.open(ServerBackupsDialog, {
      context: { serverId: s.id, serverName: s.displayName },
      contentClass: 'sm:max-w-[640px]',
    });
  }

  protected start(s: ServerInstanceDto): void {
    this.api.apiServerIdStartPost(s.id).subscribe({ next: () => this.load() });
  }

  protected async stop(s: ServerInstanceDto): Promise<void> {
    const ok = await this.confirm.open({
      title: `Stop ${s.displayName}?`,
      message: 'The container stops and players are disconnected. The world is preserved.',
      confirmLabel: 'Stop',
      destructive: true,
    });
    if (!ok) return;
    this.api.apiServerIdStopPost(s.id).subscribe({ next: () => this.load() });
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

  protected stateVariant(state: string | null | undefined): 'default' | 'secondary' | 'outline' {
    return state === 'running' ? 'default' : state ? 'secondary' : 'outline';
  }

  protected isRunning(s: ServerInstanceDto): boolean {
    return s.state === 'running';
  }

  protected sourceLabel(s: ServerInstanceDto): string {
    return s.modpackRef ?? s.version ?? 'latest';
  }
}
