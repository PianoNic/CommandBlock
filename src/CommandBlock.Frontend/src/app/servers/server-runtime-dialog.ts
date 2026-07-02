import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';
import { ServerRuntimeFields } from './server-runtime-fields';

type DialogContext = { server: ServerInstanceDto; onSaved: () => void };

@Component({
  selector: 'app-server-runtime-dialog',
  imports: [HlmButtonImports, HlmDialogHeader, HlmDialogTitle, HlmDialogDescription, HlmInputImports, HlmLabelImports, ServerRuntimeFields],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Runtime settings</h3>
      <p hlmDialogDescription>
        Memory, Java version and JVM flags for <span class="font-medium">{{ ctx.server.displayName }}</span>.
        Saving recreates the container to apply them - the server restarts, but the world is kept.
      </p>
    </hlm-dialog-header>

    <div class="grid grid-cols-2 gap-3">
      <div class="col-span-2 flex flex-col gap-1.5 sm:col-span-1">
        <label hlmLabel for="rt-mem" class="text-muted-foreground text-xs uppercase tracking-wide">Memory</label>
        <input hlmInput id="rt-mem" placeholder="e.g. 4G" [value]="memory()" (input)="memory.set($any($event.target).value)" />
      </div>
    </div>

    <app-server-runtime-fields
      [javaVersion]="javaVersion()"
      (javaVersionChange)="javaVersion.set($event)"
      [useAikarFlags]="useAikarFlags()"
      (useAikarFlagsChange)="useAikarFlags.set($event)"
      [jvmArgs]="jvmArgs()"
      (jvmArgsChange)="jvmArgs.set($event)"
      [extraEnv]="extraEnv()"
      (extraEnvChange)="extraEnv.set($event)"
    />

    @if (error(); as err) {
      <p class="text-destructive text-sm">{{ err }}</p>
    }

    <div class="flex justify-end gap-2">
      <button hlmBtn variant="outline" type="button" (click)="close()" [disabled]="saving()">Cancel</button>
      <button hlmBtn type="button" (click)="save()" [disabled]="!canSave()">
        {{ saving() ? 'Applying…' : 'Save & restart' }}
      </button>
    </div>
  `,
})
export class ServerRuntimeDialog {
  private readonly ref = inject<BrnDialogRef<unknown>>(BrnDialogRef);
  protected readonly ctx = injectBrnDialogContext<DialogContext>();
  private readonly api = inject(ServerService);

  protected readonly memory = signal(this.ctx.server.memory ?? '4G');
  protected readonly javaVersion = signal(this.ctx.server.javaVersion ?? 'auto');
  protected readonly useAikarFlags = signal(this.ctx.server.useAikarFlags ?? false);
  protected readonly jvmArgs = signal(this.ctx.server.jvmArgs ?? '');
  protected readonly extraEnv = signal(this.ctx.server.extraEnv ?? '');
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly canSave = computed(() => !this.saving() && this.memory().trim() !== '');

  protected save(): void {
    if (!this.canSave()) return;
    this.saving.set(true);
    this.error.set(null);
    this.api
      .apiServerIdRuntimePut(this.ctx.server.id, {
        memory: this.memory().trim(),
        javaVersion: this.javaVersion() === 'auto' ? undefined : this.javaVersion(),
        useAikarFlags: this.useAikarFlags(),
        jvmArgs: this.jvmArgs().trim() === '' ? undefined : this.jvmArgs().trim(),
        extraEnv: this.extraEnv().trim() === '' ? undefined : this.extraEnv(),
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.ctx.onSaved();
          this.ref.close();
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.error.set(messageOf(err));
        },
      });
  }

  protected close(): void {
    this.ref.close();
  }
}

function messageOf(err: unknown): string {
  if (err && typeof err === 'object' && 'error' in err) {
    const e = (err as { error: unknown }).error;
    if (e && typeof e === 'object' && 'error' in e) return String((e as { error: unknown }).error);
    if (typeof e === 'string' && e.trim() !== '') return e;
  }
  if (err instanceof Error) return err.message;
  return 'Failed to apply settings';
}
