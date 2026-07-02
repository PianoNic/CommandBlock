import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DatePipe, DOCUMENT } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideDownload, lucideHistory, lucideTrash2, lucidePlus, lucideArchive, lucideClock } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { ServerService } from '../api/api/server.service';
import { BackupEntryDto } from '../api/model/backupEntryDto';
import { BackupScheduleDto } from '../api/model/backupScheduleDto';
import { environment } from '../shared/environments/environment';

type DialogContext = { serverId: string; serverName: string };

@Component({
  selector: 'app-server-backups-dialog',
  imports: [
    DatePipe,
    NgIcon,
    HlmButtonImports,
    HlmInputImports,
    HlmCheckboxImports,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
  ],
  providers: [provideIcons({ lucideDownload, lucideHistory, lucideTrash2, lucidePlus, lucideArchive, lucideClock })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex max-h-[80svh] flex-col gap-4 overflow-y-auto' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Backups - {{ ctx.serverName }}</h3>
      <p hlmDialogDescription>
        Stored in the configured S3/SeaweedFS bucket (world flushed via RCON first). A <strong>World</strong>
        backup grabs just the world; a <strong>Server</strong> backup is a full dump that can restore this
        server or spin up a brand-new one.
      </p>
    </hlm-dialog-header>

    <div class="flex flex-wrap items-center justify-between gap-2">
      <span class="text-muted-foreground text-xs">
        {{ backups().length }} backup{{ backups().length === 1 ? '' : 's' }}
      </span>
      <div class="flex items-center gap-1.5">
        <button hlmBtn size="sm" variant="outline" type="button" (click)="create('world')" [disabled]="busy()" title="Back up just the world folder">
          <ng-icon name="lucidePlus" size="14" /> World backup
        </button>
        <button hlmBtn size="sm" type="button" (click)="create('server')" [disabled]="busy()" title="Full dump - restorable and can seed a new server">
          <ng-icon name="lucidePlus" size="14" /> Server backup
        </button>
      </div>
    </div>
    @if (creating()) { <p class="text-muted-foreground text-xs">Creating backup…</p> }

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
              <span class="inline-flex items-center gap-1.5">
                <span class="shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide"
                  [class.bg-primary]="b.kind === 'Server'" [class.text-primary-foreground]="b.kind === 'Server'"
                  [class.bg-secondary]="b.kind !== 'Server'" [class.text-muted-foreground]="b.kind !== 'Server'">
                  {{ b.kind === 'Server' ? 'Server' : 'World' }}
                </span>
                <span class="truncate font-mono text-sm">{{ b.fileName }}</span>
              </span>
              <span class="text-muted-foreground text-xs">
                {{ b.createdAt | date: 'medium' }} · {{ humanSize(b) }}
              </span>
            </div>
            <div class="flex items-center gap-1.5">
              @if (b.kind === 'Server') {
                <button hlmBtn size="sm" variant="outline" type="button" (click)="createServerFrom(b)" [disabled]="busy()" title="Create a new server from this backup">
                  <ng-icon name="lucidePlus" size="13" />
                  New server
                </button>
              }
              <button hlmBtn size="sm" variant="outline" type="button" (click)="restore(b)" [disabled]="busy()" title="Restore this backup over this server">
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

    <div class="flex flex-col gap-2 border-t pt-3">
      <span class="inline-flex items-center gap-1.5 text-sm font-medium">
        <ng-icon name="lucideClock" size="14" /> Scheduled backups
      </span>
      <div class="flex flex-wrap items-center gap-1.5">
        @for (p of presets; track p.cron) {
          <button hlmBtn size="sm" variant="outline" type="button" (click)="addSchedule(p.cron)" [disabled]="busy()">
            <ng-icon name="lucidePlus" size="12" /> {{ p.label }}
          </button>
        }
      </div>
      <!-- Custom cron expression -->
      <div class="flex items-center gap-1.5">
        <input hlmInput class="h-8 flex-1 font-mono text-xs" placeholder="Custom cron - e.g. 30 4 * * 1-5 (min hour day month weekday, UTC)"
          [value]="customCron()" (input)="customCron.set($any($event.target).value)" (keydown.enter)="addCustom()" />
        <button hlmBtn size="sm" variant="outline" type="button" class="h-8 shrink-0" (click)="addCustom()" [disabled]="busy() || customCron().trim() === ''">
          <ng-icon name="lucidePlus" size="12" /> Add
        </button>
      </div>
      @if (schedules().length > 0) {
        <ul class="divide-border divide-y rounded-md border">
          @for (s of schedules(); track s.id) {
            <li class="flex items-center gap-3 p-2">
              <hlm-checkbox [checked]="s.enabled" (checkedChange)="toggleSchedule(s, $event)" />
              <div class="min-w-0 flex-1">
                <span class="font-mono text-xs">{{ s.cronExpression }}</span>
                <span class="text-muted-foreground text-xs"> · {{ describeCron(s.cronExpression) }}</span>
                <div class="text-muted-foreground text-[11px]">
                  @if (s.enabled && s.nextRunAt) { next {{ s.nextRunAt | date: 'short' }} } @else { paused }
                  @if (s.lastStatus === 'error') { · <span class="text-destructive">last run failed</span> }
                </div>
              </div>
              <button hlmBtn size="sm" variant="ghost" type="button" (click)="removeSchedule(s)" title="Delete schedule">
                <ng-icon name="lucideTrash2" size="13" />
              </button>
            </li>
          }
        </ul>
      } @else {
        <p class="text-muted-foreground text-xs">No schedules - pick a preset above to back up automatically (times are UTC).</p>
      }
    </div>
  `,
})
export class ServerBackupsDialog {
  protected readonly ctx = injectBrnDialogContext<DialogContext>();
  private readonly ref = inject<BrnDialogRef<unknown>>(BrnDialogRef);
  private readonly api = inject(ServerService);
  private readonly confirm = inject(ConfirmService);
  private readonly http = inject(HttpClient);
  private readonly doc = inject(DOCUMENT);

  protected readonly backups = signal<ReadonlyArray<BackupEntryDto>>([]);
  protected readonly schedules = signal<ReadonlyArray<BackupScheduleDto>>([]);
  protected readonly customCron = signal('');
  protected readonly loading = signal(false);
  protected readonly creating = signal(false);
  protected readonly working = signal(false);
  protected readonly error = signal<string | null>(null);

  // Common cron presets (UTC). describeCron() maps a stored cron back to its label.
  protected readonly presets = [
    { label: 'Hourly', cron: '0 * * * *' },
    { label: 'Every 6h', cron: '0 */6 * * *' },
    { label: 'Daily 3am', cron: '0 3 * * *' },
    { label: 'Weekly', cron: '0 4 * * 0' },
  ] as const;

  protected busy = () => this.creating() || this.working() || this.loading();

  constructor() {
    this.load();
    this.loadSchedules();
  }

  protected loadSchedules(): void {
    this.api.apiServerIdBackupSchedulesGet(this.ctx.serverId).subscribe({ next: (rows) => this.schedules.set(rows) });
  }

  protected addSchedule(cron: string, onSuccess?: () => void): void {
    this.working.set(true);
    this.error.set(null);
    this.api.apiServerIdBackupSchedulesPost(this.ctx.serverId, { cronExpression: cron }).subscribe({
      next: () => {
        this.working.set(false);
        this.loadSchedules();
        onSuccess?.();
      },
      error: (err: unknown) => {
        this.working.set(false);
        this.error.set(messageOf(err));
      },
    });
  }

  protected addCustom(): void {
    const cron = this.customCron().trim();
    if (cron === '') return;
    // Clears the field only on success, so an invalid cron (400 from the API) keeps what you typed.
    this.addSchedule(cron, () => this.customCron.set(''));
  }

  protected toggleSchedule(s: BackupScheduleDto, enabled: boolean): void {
    this.api.apiServerBackupSchedulesScheduleIdPatch(s.id, { enabled }).subscribe({ next: () => this.loadSchedules() });
  }

  protected async removeSchedule(s: BackupScheduleDto): Promise<void> {
    const ok = await this.confirm.open({
      title: 'Delete schedule?',
      message: `Stop the "${s.cronExpression}" scheduled backup. Existing backups are kept.`,
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;
    this.api.apiServerBackupSchedulesScheduleIdDelete(s.id).subscribe({ next: () => this.loadSchedules() });
  }

  protected describeCron(cron: string): string {
    return this.presets.find((p) => p.cron === cron)?.label ?? 'custom';
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

  protected create(kind: 'world' | 'server'): void {
    this.creating.set(true);
    this.error.set(null);
    this.api.apiServerIdBackupsPost(this.ctx.serverId, kind).subscribe({
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

  /// Spins up a brand-new server from a full server backup. Prompts for the new name + hostname.
  protected createServerFrom(b: BackupEntryDto): void {
    const name = window.prompt('Name for the new server:', 'Restored ' + this.ctx.serverName);
    if (!name?.trim()) return;
    const hostname = window.prompt('Hostname for the new server (must be unique, e.g. restored.example.com):');
    if (!hostname?.trim()) return;
    this.working.set(true);
    this.error.set(null);
    this.api.apiServerBackupsBackupIdCreateServerPost(b.id, { displayName: name.trim(), hostname: hostname.trim() }).subscribe({
      next: () => { this.working.set(false); this.ref.close(); },
      error: (err: unknown) => { this.working.set(false); this.error.set(messageOf(err)); },
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
