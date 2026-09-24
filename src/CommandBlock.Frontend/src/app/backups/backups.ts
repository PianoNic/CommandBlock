import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { forkJoin, of } from 'rxjs';
import { catchError, map, switchMap } from 'rxjs/operators';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArchive, lucideDownload, lucideHistory, lucideTrash2, lucidePlus } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { ServerService } from '../api/api/server.service';
import { BackupEntryDto } from '../api/model/backupEntryDto';
import { BackupCreateDialog } from './backup-create-dialog';
import { LocalDatePipe } from '../shared/pipes/local-date.pipe';
import { formatBytes } from '../shared/utils/format';
import { toastError } from '../shared/utils/errors';
import { toast } from '@spartan-ng/brain/sonner';
import { HttpClient } from '@angular/common/http';
import { environment } from '../shared/environments/environment';

type Row = BackupEntryDto & { serverName: string };

@Component({
  selector: 'app-backups',
  imports: [LocalDatePipe, NgIcon, HlmButtonImports, HlmTableImports, ContentHeader],
  providers: [provideIcons({ lucideArchive, lucideDownload, lucideHistory, lucideTrash2, lucidePlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />
    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <h3 class="text-sm font-medium">Backups</h3>
        <div class="flex items-center gap-2">
          <button hlmBtn size="sm" (click)="createBackup()">
            <ng-icon name="lucidePlus" size="16" /> Create backup
          </button>
          <button hlmBtn variant="outline" size="sm" (click)="load()" [disabled]="loading()">
            {{ loading() ? 'Loading…' : 'Refresh' }}
          </button>
        </div>
      </header>

      <div class="min-h-0 flex-1 overflow-auto px-4">
        @if (failed().length > 0 && !loading()) {
          <div class="text-destructive flex items-center gap-2 py-3 text-sm" role="alert">
            <p>Couldn't load backups for {{ failed().join(', ') }}.</p>
            <button hlmBtn size="sm" variant="outline" (click)="load()">Retry</button>
          </div>
        }
        @if (rows().length === 0 && !loading()) {
          @if (failed().length === 0) {
            <div class="text-muted-foreground flex h-full flex-col items-center justify-center gap-3 text-center text-sm">
              <ng-icon name="lucideArchive" size="32" class="opacity-50" />
              <p>No backups yet. Use <strong>Create backup</strong> to make your first one.</p>
            </div>
          }
        } @else {
          <table hlmTable class="w-full">
            <thead hlmTHead>
              <tr hlmTr>
                <th hlmTh>Server</th><th hlmTh>Type</th><th hlmTh class="max-md:hidden">Backup</th><th hlmTh>Size</th><th hlmTh>Created</th>
                <th hlmTh class="text-right">Actions</th>
              </tr>
            </thead>
            <tbody hlmTBody>
              @for (b of rows(); track b.id) {
                <tr hlmTr>
                  <td hlmTd class="font-medium">{{ b.serverName }}</td>
                  <td hlmTd class="text-xs">{{ b.kind === 'Server' ? 'Full server' : 'World' }}</td>
                  <td hlmTd class="font-mono text-xs max-md:hidden">{{ b.fileName }}</td>
                  <td hlmTd class="font-mono text-xs">{{ size(b) }}</td>
                  <td hlmTd class="text-muted-foreground text-xs">{{ b.createdAt | localDate: 'medium' }}</td>
                  <td hlmTd>
                    <div class="flex items-center justify-end gap-1">
                      <button hlmBtn size="sm" variant="outline" (click)="restore(b)" [disabled]="busy()" title="Restore this backup over its server">
                        <ng-icon name="lucideHistory" size="13" /> Restore
                      </button>
                      <button hlmBtn size="sm" variant="ghost" (click)="download(b)" [disabled]="busy()" title="Download" [attr.aria-label]="'Download ' + b.fileName">
                        <ng-icon name="lucideDownload" size="13" />
                      </button>
                      <button hlmBtn size="sm" variant="ghost" (click)="remove(b)" [disabled]="busy()" title="Delete" [attr.aria-label]="'Delete ' + b.fileName">
                        <ng-icon name="lucideTrash2" size="13" />
                      </button>
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>
    </section>
  `,
})
export class Backups {
  private readonly api = inject(ServerService);
  private readonly confirm = inject(ConfirmService);
  private readonly dialog = inject(HlmDialogService);
  private readonly http = inject(HttpClient);

  protected createBackup(): void {
    this.dialog.open(BackupCreateDialog, {
      context: { onCreated: () => this.load() },
      contentClass: 'sm:max-w-[480px]',
    });
  }

  protected readonly rows = signal<ReadonlyArray<Row>>([]);
  protected readonly loading = signal(false);
  /// Servers whose backups couldn't be fetched - shown as an error rather than silently missing rows.
  protected readonly failed = signal<ReadonlyArray<string>>([]);
  protected readonly working = signal(false);
  protected busy = () => this.loading() || this.working();

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set([]);
    this.api
      .apiServerGet()
      .pipe(
        switchMap((servers) =>
          servers.length === 0
            ? of([] as Row[])
            : forkJoin(
                servers.map((s) =>
                  this.api.apiServerIdBackupsGet(s.id).pipe(
                    map((bs) => bs.map((b) => ({ ...b, serverName: s.displayName }) as Row)),
                    catchError(() => { this.failed.update((f) => [...f, s.displayName]); return of([] as Row[]); }),
                  ),
                ),
              ).pipe(map((arrs) => arrs.flat().sort((a, b) => b.createdAt.localeCompare(a.createdAt)))),
        ),
      )
      .subscribe({
        next: (rows) => { this.rows.set(rows); this.loading.set(false); },
        error: (err: unknown) => { this.loading.set(false); toastError(err, "Couldn't load backups."); },
      });
  }

  protected async restore(b: Row): Promise<void> {
    const ok = await this.confirm.open({
      title: `Restore ${b.fileName}?`,
      message: `${b.serverName} stops, its ${b.kind === 'Server' ? 'files are' : 'world is'} replaced with this backup, and it starts again. Anything changed since the backup is lost.`,
      confirmLabel: 'Restore backup',
      destructive: true,
    });
    if (!ok) return;
    this.working.set(true);
    this.api.apiServerBackupsBackupIdRestorePost(b.id).subscribe({
      next: () => { this.working.set(false); toast.success(`Restored ${b.serverName}. It's starting again.`); },
      error: (err: unknown) => { this.working.set(false); toastError(err, "Couldn't restore the backup."); },
    });
  }

  protected async remove(b: Row): Promise<void> {
    const ok = await this.confirm.open({
      title: `Delete ${b.fileName}?`,
      message: 'This backup is deleted permanently and cannot be restored afterwards.',
      confirmLabel: 'Delete backup',
      destructive: true,
    });
    if (!ok) return;
    this.working.set(true);
    this.api.apiServerBackupsBackupIdDelete(b.id).subscribe({
      next: () => { this.working.set(false); toast.success('Backup deleted.'); this.load(); },
      error: (err: unknown) => { this.working.set(false); toastError(err, "Couldn't delete the backup."); },
    });
  }

  protected download(b: Row): void {
    this.working.set(true);
    this.http.get(`${environment.apiBaseUrl}/api/Server/backups/${b.id}/download`, { responseType: 'blob' }).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url; a.download = b.fileName; a.click();
        URL.revokeObjectURL(url);
        this.working.set(false);
      },
      error: (err: unknown) => { this.working.set(false); toastError(err, "Couldn't download the backup."); },
    });
  }

  protected size(b: Row): string { return formatBytes(b.sizeBytes); }
}
