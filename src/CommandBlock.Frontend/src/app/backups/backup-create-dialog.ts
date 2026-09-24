import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';
import { messageOf } from '../shared/utils/errors';
import { serverAddress } from '../shared/utils/server-address';
import { toast } from '@spartan-ng/brain/sonner';

type DialogContext = { onCreated: () => void };

@Component({
  selector: 'app-backup-create-dialog',
  imports: [HlmButtonImports, HlmDialogHeader, HlmDialogTitle, HlmDialogDescription, HlmLabelImports, HlmSelectImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Create backup</h3>
      <p hlmDialogDescription>
        Saves a copy to your backup storage. The server keeps running; it saves the world first so the copy is consistent.
      </p>
    </hlm-dialog-header>

    <form class="flex flex-col gap-4" (submit)="$event.preventDefault(); create()">
    <div class="flex flex-col gap-1.5">
      <label hlmLabel id="backup-server-label" class="text-muted-foreground text-xs uppercase tracking-wide">Server</label>
      <hlm-select aria-labelledby="backup-server-label" [value]="serverId()" (valueChange)="serverId.set($event)" [itemToString]="serverLabel">
        <hlm-select-trigger class="w-full"><hlm-select-value placeholder="Pick a server…" /></hlm-select-trigger>
        <hlm-select-content *hlmSelectPortal>
          @for (s of servers(); track s.id) {
            <hlm-select-item [value]="s.id">{{ s.displayName }} ({{ address(s) }})</hlm-select-item>
          }
        </hlm-select-content>
      </hlm-select>
    </div>

    <fieldset class="flex flex-col gap-1.5">
      <legend class="text-muted-foreground mb-1.5 text-xs uppercase tracking-wide">What to back up</legend>
      @for (k of kinds; track k.value) {
        <label class="hover:bg-accent flex cursor-pointer items-start gap-2 rounded-md border p-2 text-sm" [class.border-primary]="kind() === k.value">
          <input type="radio" name="backup-kind" class="mt-1" [value]="k.value" [checked]="kind() === k.value" (change)="kind.set(k.value)" />
          <span><span class="font-medium">{{ k.label }}</span><br /><span class="text-muted-foreground text-xs">{{ k.hint }}</span></span>
        </label>
      }
    </fieldset>

    @if (error(); as e) { <p class="text-destructive text-sm">{{ e }}</p> }

    <div class="flex justify-end gap-2">
      <button hlmBtn variant="outline" type="button" (click)="close()" [disabled]="creating()">Cancel</button>
      <button hlmBtn type="submit" [disabled]="!canCreate()">
        {{ creating() ? 'Creating…' : 'Create backup' }}
      </button>
    </div>
    </form>
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

  // The select binds each option to the server id, so the trigger would show the raw guid without a
  // value->label mapping. Resolve the id back to the server's display name (+ hostname).
  protected readonly serverLabel = (id: string | null): string => {
    const s = this.servers().find((x) => x.id === id);
    return s ? `${s.displayName} (${serverAddress(s)})` : '';
  };

  protected readonly address = serverAddress;
  protected readonly kind = signal<'world' | 'server'>('world');
  protected readonly kinds = [
    { value: 'world', label: 'World only', hint: 'The map and player progress. Small and quick - good for regular snapshots.' },
    { value: 'server', label: 'Full server', hint: 'Everything: world, plugins, mods and config. Can also create a copy of this server.' },
  ] as const;

  constructor() {
    this.api.apiServerGet().subscribe((rows) => this.servers.set(rows.filter((s) => s.isManaged && s.containerName)));
  }

  protected create(): void {
    const id = this.serverId();
    if (!id || this.creating()) return;
    this.creating.set(true);
    this.error.set(null);
    this.api.apiServerIdBackupsPost(id, this.kind()).subscribe({
      next: () => {
        this.creating.set(false);
        toast.success(this.kind() === 'world' ? 'World backup created.' : 'Server backup created.');
        this.ctx.onCreated();
        this.ref.close();
      },
      error: (err: unknown) => { this.creating.set(false); this.error.set(messageOf(err, 'Backup failed.')); },
    });
  }

  protected close(): void { this.ref.close(); }
}
