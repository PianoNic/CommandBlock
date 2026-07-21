import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { MinecraftVersionsService } from '../api/api/minecraftVersions.service';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';
import { ServerRuntimeFields } from './server-runtime-fields';

/// The Runtime section of the server-settings modal: memory, Java version and JVM flags. Saving
/// recreates the container (restart, world kept). Standalone form for the tabbed modal.
@Component({
  selector: 'app-server-runtime-form',
  imports: [HlmButtonImports, HlmInputImports, HlmLabelImports, HlmSelectImports, ServerRuntimeFields],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <p class="text-muted-foreground text-xs">Memory, Java version and JVM flags. Saving recreates the container to apply them - the server restarts, but the world is kept.</p>

    <div class="grid grid-cols-2 gap-3">
      <div class="col-span-2 flex flex-col gap-1.5 sm:col-span-1">
        <label hlmLabel for="rt-mem" class="text-muted-foreground text-xs uppercase tracking-wide">Memory</label>
        <input hlmInput id="rt-mem" placeholder="e.g. 4G" [value]="memory()" (input)="memory.set($any($event.target).value)" />
      </div>

      @if (!isModpack()) {
        <div class="col-span-2 flex flex-col gap-1.5 sm:col-span-1">
          <label hlmLabel for="rt-version" class="text-muted-foreground text-xs uppercase tracking-wide">Minecraft version</label>
          <hlm-select [value]="version()" (valueChange)="version.set($event ?? LATEST)" [itemToString]="versionLabel">
            <hlm-select-trigger id="rt-version" class="w-full"><hlm-select-value placeholder="Latest" /></hlm-select-trigger>
            <hlm-select-content *hlmSelectPortal>
              <hlm-select-item [value]="LATEST">Latest</hlm-select-item>
              @for (v of versions(); track v) {
                <hlm-select-item [value]="v">{{ v }}</hlm-select-item>
              }
            </hlm-select-content>
          </hlm-select>
          @if (versionChanged()) {
            <span class="text-destructive text-xs">
              Changing version updates the server in place and keeps the world. Downgrading can corrupt a world - back it up first.
            </span>
          }
        </div>
      }
    </div>

    <app-server-runtime-fields
      [javaVersion]="javaVersion()"
      (javaVersionChange)="javaVersion.set($event)"
      [useAikarFlags]="useAikarFlags()"
      (useAikarFlagsChange)="useAikarFlags.set($event)"
      [allowAnyClientVersion]="allowAnyClientVersion()"
      (allowAnyClientVersionChange)="allowAnyClientVersion.set($event)"
      [jvmArgs]="jvmArgs()"
      (jvmArgsChange)="jvmArgs.set($event)"
      [extraEnv]="extraEnv()"
      (extraEnvChange)="extraEnv.set($event)"
    />

    @if (error(); as err) { <p class="text-destructive text-sm">{{ err }}</p> }

    <div class="flex justify-end gap-2">
      <button hlmBtn size="sm" type="button" (click)="save()" [disabled]="!canSave()">
        {{ saving() ? 'Applying…' : 'Save & restart' }}
      </button>
    </div>
  `,
})
export class ServerRuntimeForm implements OnInit {
  readonly server = input.required<ServerInstanceDto>();
  readonly saved = output<void>();

  private readonly api = inject(ServerService);
  private readonly versionsApi = inject(MinecraftVersionsService);

  /// Sentinel for "no explicit version" - itzg then pulls the latest release.
  protected readonly LATEST = 'latest';
  protected readonly versions = signal<ReadonlyArray<string>>([]);
  protected readonly version = signal<string>('latest');
  private readonly originalVersion = signal<string>('latest');
  protected readonly isModpack = computed(() => MODPACK_TYPES.includes(this.server().serverType));
  protected readonly versionChanged = computed(() => this.version() !== this.originalVersion());
  protected readonly versionLabel = (v: string | null): string => (v && v !== 'latest' ? v : 'Latest');

  protected readonly memory = signal('4G');
  protected readonly javaVersion = signal('auto');
  protected readonly useAikarFlags = signal(false);
  protected readonly allowAnyClientVersion = signal(false);
  protected readonly jvmArgs = signal('');
  protected readonly extraEnv = signal('');
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly canSave = computed(() => !this.saving() && this.memory().trim() !== '');

  ngOnInit(): void {
    const s = this.server();
    this.memory.set(s.memory ?? '4G');
    this.javaVersion.set(s.javaVersion ?? 'auto');
    this.useAikarFlags.set(s.useAikarFlags ?? false);
    this.allowAnyClientVersion.set(s.allowAnyClientVersion ?? false);
    this.jvmArgs.set(s.jvmArgs ?? '');
    this.extraEnv.set(s.extraEnv ?? '');
    this.version.set(s.version ?? this.LATEST);
    this.originalVersion.set(s.version ?? this.LATEST);
    if (!this.isModpack()) {
      this.versionsApi.apiMinecraftVersionsGet().subscribe({
        next: (v) => this.versions.set(v),
        error: () => this.versions.set([]),
      });
    }
  }

  protected save(): void {
    if (!this.canSave()) return;
    this.saving.set(true);
    this.error.set(null);
    this.api
      .apiServerIdRuntimePut(this.server().id, {
        memory: this.memory().trim(),
        version: this.version() === this.LATEST ? undefined : this.version(),
        javaVersion: this.javaVersion() === 'auto' ? undefined : this.javaVersion(),
        useAikarFlags: this.useAikarFlags(),
        allowAnyClientVersion: this.allowAnyClientVersion(),
        jvmArgs: this.jvmArgs().trim() === '' ? undefined : this.jvmArgs().trim(),
        extraEnv: this.extraEnv().trim() === '' ? undefined : this.extraEnv(),
      })
      .subscribe({
        next: () => { this.saving.set(false); this.saved.emit(); },
        error: (err: unknown) => { this.saving.set(false); this.error.set(messageOf(err)); },
      });
  }
}

const MODPACK_TYPES = ['MODRINTH', 'CURSEFORGE', 'AUTO_CURSEFORGE', 'FTBA'];

function messageOf(err: unknown): string {
  if (err && typeof err === 'object' && 'error' in err) {
    const e = (err as { error: unknown }).error;
    if (e && typeof e === 'object' && 'error' in e) return String((e as { error: unknown }).error);
    if (typeof e === 'string' && e.trim() !== '') return e;
  }
  if (err instanceof Error) return err.message;
  return 'Failed to apply settings';
}
