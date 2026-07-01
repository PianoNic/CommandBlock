import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideServer, lucideGlobe } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';
import { ServerCreateDialog } from './server-create-dialog';

@Component({
  selector: 'app-servers',
  imports: [
    ContentHeader,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmTableImports,
  ],
  providers: [provideIcons({ lucidePlus, lucideServer, lucideGlobe })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './servers.html',
})
export class Servers {
  private readonly api = inject(ServerService);
  private readonly dialog = inject(HlmDialogService);

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

  // Map Docker container state to a badge variant. Running is the happy path; anything else
  // is muted/secondary so a stopped or unknown server reads as "needs attention".
  protected stateVariant(state: string | null | undefined): 'default' | 'secondary' | 'outline' {
    return state === 'running' ? 'default' : state ? 'secondary' : 'outline';
  }

  // What the server is running: the version for plain loaders, or the pack ref for modpacks.
  protected sourceLabel(s: ServerInstanceDto): string {
    return s.modpackRef ?? s.version ?? 'latest';
  }
}
