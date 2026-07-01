import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';

type DialogContext = { onCreated: () => void };

@Component({
  selector: 'app-backup-create-dialog',
  imports: [HlmButtonImports, HlmDialogHeader, HlmDialogTitle, HlmDialogDescription, HlmLabelImports, HlmSelectImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Create world backup</h3>
      <p hlmDialogDescription>
        Archives the selected server's world (RCON-flushed for a clean snapshot) into the backup bucket.
      </p>
    </hlm-dialog-header>

    <div class="flex flex-col gap-1.5">
      <label hlmLabel class="text-muted-foreground text-xs uppercase tracking-wide">Server</label>
      <hlm-select [value]="serverId()" (valueChange)="serverId.set($event)">
        <hlm-select-trigger class="w-full"><hlm-select-value placeholder="Pick a server…" /></hlm-select-trigger>
        <hlm-select-content *hlmSelectPortal>
          @for (s of servers(); track s.id) {
            <hlm-select-item [value]="s.id">{{ s.displayName }} ({{ s.hostname }})</hlm-select-item>
          }
        </hlm-select-content>
      </hlm-select>
    </div>

    @if (error(); as e) { <p class="text-destructive text-sm">{{ e }}</p> }

    <div class="flex justify-end gap-2">
      <button hlmBtn variant="outline" type="button" (click)="close()" [disabled]="creating()">Cancel</button>
      <button hlmBtn type="button" (click)="create()" [disabled]="!canCreate()">
        {{ creating() ? 'Creating…' : 'Create backup' }}
      </button>
    </div>
  `,
})
export class BackupCreateDialog {
  private readonly ref = inject<BrnDialogRef<unknown>>(BrnDialogRef);
  private readonly ctx = injectBrnDialogContext<DialogContext>();
  private readonly api = inject(ServerService);

  protected readonly servers = signal<ReadonlyArray<ServerInstanceDto>>([]);
  protected readonly serverId = signal<string | null>(null);
  protected readonly creating = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly canCreate = computed(() => !this.creating() && !!this.serverId());

  constructor() {
    this.api.apiServerGet().subscribe((rows) => this.servers.set(rows.filter((s) => s.isManaged && s.containerName)));
  }

  protected create(): void {
    const id = this.serverId();
    if (!id) return;
    this.creating.set(true);
    this.error.set(null);
    this.api.apiServerIdBackupsPost(id).subscribe({
      next: () => { this.creating.set(false); this.ctx.onCreated(); this.ref.close(); },
      error: (err: unknown) => { this.creating.set(false); this.error.set(messageOf(err)); },
    });
  }

  protected close(): void { this.ref.close(); }
}

function messageOf(err: unknown): string {
  if (err && typeof err === 'object' && 'error' in err) {
    const e = (err as { error: unknown }).error;
    if (e && typeof e === 'object' && 'error' in e) return String((e as { error: unknown }).error);
  }
  return err instanceof Error ? err.message : 'Backup failed';
}
