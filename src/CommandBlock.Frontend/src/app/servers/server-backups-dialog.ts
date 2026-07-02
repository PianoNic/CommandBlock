import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DatePipe, DOCUMENT } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideDownload, lucideHistory, lucideTrash2, lucidePlus, lucideArchive } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { ServerService } from '../api/api/server.service';
import { BackupEntryDto } from '../api/model/backupEntryDto';
import { environment } from '../shared/environments/environment';

type DialogContext = { serverId: string; serverName: string };

@Component({
  selector: 'app-server-backups-dialog',
  imports: [
    DatePipe,
    NgIcon,
    HlmButtonImports,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
  ],
  providers: [provideIcons({ lucideDownload, lucideHistory, lucideTrash2, lucidePlus, lucideArchive })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Backups - {{ ctx.serverName }}</h3>
      <p hlmDialogDescription>
        World snapshots stored in the configured S3/SeaweedFS bucket. Creating a backup flushes the
        world via RCON first; restoring stops the server, extracts the world, and starts it again.
      </p>
    </hlm-dialog-header>

    <div class="flex items-center justify-between gap-2">
      <span class="text-muted-foreground text-xs">
        {{ backups().length }} backup{{ backups().length === 1 ? '' : 's' }}
      </span>
      <button hlmBtn size="sm" type="button" (click)="create()" [disabled]="busy()">
        <ng-icon name="lucidePlus" size="14" />
        {{ creating() ? 'Creating…' : 'Create backup' }}
      </button>
    </div>

    @if (error(); as err) {
      <p class="text-destructive text-sm">{{ err }}</p>
    }

    @if (backups().length === 0 && !loading()) {
      <div class="text-muted-foreground flex flex-col items-center gap-2 py-8 text-center text-sm">
        <ng-icon name="lucideArchive" size="28" class="opacity-50" />
        <p>No backups yet.</p>
      </div>
    } @else {
      <ul class="divide-border max-h-80 divide-y overflow-auto">
        @for (b of backups(); track b.id) {
          <li class="flex items-center justify-between gap-3 py-2">
            <div class="min-w-0 flex flex-col">
              <span class="truncate font-mono text-sm">{{ b.fileName }}</span>
              <span class="text-muted-foreground text-xs">
                {{ b.createdAt | date: 'medium' }} · {{ humanSize(b) }}
              </span>
            </div>
            <div class="flex items-center gap-1.5">
              <button hlmBtn size="sm" variant="outline" type="button" (click)="restore(b)" [disabled]="busy()" title="Restore this backup">
                <ng-icon name="lucideHistory" size="13" />
                Restore
              </button>
              <button hlmBtn size="sm" variant="ghost" type="button" (click)="download(b)" [disabled]="busy()" title="Download this backup">
                <ng-icon name="lucideDownload" size="13" />
              </button>
              <button hlmBtn size="sm" variant="ghost" type="button" (click)="remove(b)" [disabled]="busy()" title="Delete this backup">
                <ng-icon name="lucideTrash2" size="13" />
              </button>
            </div>
          </li>
        }
      </ul>
    }
  `,
})
export class ServerBackupsDialog {
  protected readonly ctx = injectBrnDialogContext<DialogContext>();
  private readonly api = inject(ServerService);
  private readonly confirm = inject(ConfirmService);
  private readonly http = inject(HttpClient);
  private readonly doc = inject(DOCUMENT);

  protected readonly backups = signal<ReadonlyArray<BackupEntryDto>>([]);
  protected readonly loading = signal(false);
  protected readonly creating = signal(false);
  protected readonly working = signal(false);
  protected readonly error = signal<string | null>(null);

  protected busy = () => this.creating() || this.working() || this.loading();

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.api.apiServerIdBackupsGet(this.ctx.serverId).subscribe({
      next: (rows) => {
        this.backups.set(rows);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load backups.');
        this.loading.set(false);
      },
    });
  }

  protected create(): void {
    this.creating.set(true);
    this.error.set(null);
    this.api.apiServerIdBackupsPost(this.ctx.serverId).subscribe({
      next: () => {
        this.creating.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.error.set(messageOf(err));
      },
    });
  }

  protected async restore(b: BackupEntryDto): Promise<void> {
    const ok = await this.confirm.open({
      title: `Restore ${b.fileName}?`,
      message: 'The server will be stopped, its world replaced with this backup, and started again. Current world data is overwritten.',
      confirmLabel: 'Restore',
      destructive: true,
    });
    if (!ok) return;
    this.working.set(true);
    this.error.set(null);
    this.api.apiServerBackupsBackupIdRestorePost(b.id).subscribe({
      next: () => this.working.set(false),
      error: (err: unknown) => {
        this.working.set(false);
        this.error.set(messageOf(err));
      },
    });
  }

  protected download(b: BackupEntryDto): void {
    this.working.set(true);
    this.error.set(null);
    // The OIDC interceptor adds the bearer token (apiBaseUrl is a secureRoute); we fetch the archive
    // as a blob and hand it to the browser as a file download.
    this.http
      .get(`${environment.apiBaseUrl}/api/Server/backups/${b.id}/download`, { responseType: 'blob' })
      .subscribe({
        next: (blob) => {
          const url = URL.createObjectURL(blob);
          const a = this.doc.createElement('a');
          a.href = url;
          a.download = b.fileName;
          a.click();
          URL.revokeObjectURL(url);
          this.working.set(false);
        },
        error: (err: unknown) => {
          this.working.set(false);
          this.error.set(messageOf(err));
        },
      });
  }

  protected async remove(b: BackupEntryDto): Promise<void> {
    const ok = await this.confirm.open({
      title: `Delete ${b.fileName}?`,
      message: 'This permanently removes the backup from object storage.',
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;
    this.working.set(true);
    this.api.apiServerBackupsBackupIdDelete(b.id).subscribe({
      next: () => {
        this.working.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.working.set(false);
        this.error.set(messageOf(err));
      },
    });
  }

  protected humanSize(b: BackupEntryDto): string {
    let n = Number(b.sizeBytes as unknown as number) || 0;
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    let u = 0;
    while (n >= 1024 && u < units.length - 1) {
      n /= 1024;
      u++;
    }
    return `${n.toFixed(u === 0 ? 0 : 1)} ${units[u]}`;
  }
}

function messageOf(err: unknown): string {
  if (err && typeof err === 'object' && 'error' in err) {
    const e = (err as { error: unknown }).error;
    if (e && typeof e === 'object' && 'error' in e) return String((e as { error: unknown }).error);
    if (typeof e === 'string' && e.trim() !== '') return e;
  }
  if (err instanceof Error) return err.message;
  return 'Request failed';
}
