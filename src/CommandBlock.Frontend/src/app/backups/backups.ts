import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { forkJoin, of } from 'rxjs';
import { catchError, map, switchMap } from 'rxjs/operators';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArchive, lucideDownload, lucideTrash2, lucidePlus } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { ServerService } from '../api/api/server.service';
import { BackupEntryDto } from '../api/model/backupEntryDto';
import { BackupCreateDialog } from './backup-create-dialog';

type Row = BackupEntryDto & { serverName: string };

@Component({
  selector: 'app-backups',
  imports: [DatePipe, NgIcon, HlmButtonImports, HlmTableImports, ContentHeader],
  providers: [provideIcons({ lucideArchive, lucideDownload, lucideTrash2, lucidePlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />
    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <h3 class="text-sm font-medium">World backups</h3>
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
        @if (rows().length === 0 && !loading()) {
          <div class="text-muted-foreground flex flex-col items-center gap-3 py-16 text-center text-sm">
            <ng-icon name="lucideArchive" size="32" class="opacity-50" />
            <p>No backups yet. Create one from a server's Backups button.</p>
          </div>
        } @else {
          <table hlmTable class="w-full">
            <thead hlmTHead>
              <tr hlmTr>
                <th hlmTh>Server</th><th hlmTh>Backup</th><th hlmTh>Size</th><th hlmTh>Created</th>
                <th hlmTh class="text-right">Actions</th>
              </tr>
            </thead>
            <tbody hlmTBody>
              @for (b of rows(); track b.id) {
                <tr hlmTr>
                  <td hlmTd class="font-medium">{{ b.serverName }}</td>
                  <td hlmTd class="font-mono text-xs">{{ b.fileName }}</td>
                  <td hlmTd class="font-mono text-xs">{{ size(b) }}</td>
                  <td hlmTd class="text-muted-foreground text-xs">{{ b.createdAt | date: 'medium' }}</td>
                  <td hlmTd>
                    <div class="flex items-center justify-end gap-1">
                      <button hlmBtn size="sm" variant="outline" (click)="restore(b)" [disabled]="busy()" title="Restore">
                        <ng-icon name="lucideDownload" size="13" /> Restore
                      </button>
                      <button hlmBtn size="sm" variant="ghost" (click)="remove(b)" [disabled]="busy()" title="Delete">
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

  protected createBackup(): void {
    this.dialog.open(BackupCreateDialog, {
      context: { onCreated: () => this.load() },
      contentClass: 'sm:max-w-[480px]',
    });
  }

  protected readonly rows = signal<ReadonlyArray<Row>>([]);
  protected readonly loading = signal(false);
  protected readonly working = signal(false);
  protected busy = () => this.loading() || this.working();

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
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
                    catchError(() => of([] as Row[])),
                  ),
                ),
              ).pipe(map((arrs) => arrs.flat().sort((a, b) => b.createdAt.localeCompare(a.createdAt)))),
        ),
      )
      .subscribe({
        next: (rows) => { this.rows.set(rows); this.loading.set(false); },
        error: () => this.loading.set(false),
      });
  }

  protected async restore(b: Row): Promise<void> {
    const ok = await this.confirm.open({
      title: `Restore ${b.fileName}?`,
      message: `${b.serverName} will be stopped, its world replaced with this backup, and started again.`,
      confirmLabel: 'Restore',
      destructive: true,
    });
    if (!ok) return;
    this.working.set(true);
    this.api.apiServerBackupsBackupIdRestorePost(b.id).subscribe({
      next: () => this.working.set(false),
      error: () => this.working.set(false),
    });
  }

  protected async remove(b: Row): Promise<void> {
    const ok = await this.confirm.open({
      title: `Delete ${b.fileName}?`,
      message: 'This permanently removes the backup from object storage.',
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;
    this.working.set(true);
    this.api.apiServerBackupsBackupIdDelete(b.id).subscribe({
      next: () => { this.working.set(false); this.load(); },
      error: () => this.working.set(false),
    });
  }

  protected size(b: Row): string {
    let n = Number(b.sizeBytes as unknown as number) || 0;
    const u = ['B', 'KB', 'MB', 'GB', 'TB'];
    let i = 0;
    while (n >= 1024 && i < u.length - 1) { n /= 1024; i++; }
    return `${n.toFixed(i === 0 ? 0 : 1)} ${u[i]}`;
  }
}
