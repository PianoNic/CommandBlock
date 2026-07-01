import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSearch, lucideDownload, lucideCheck } from '@ng-icons/lucide';
import { simpleModrinth, simpleCurseforge } from '@ng-icons/simple-icons';
import { PLATFORM_ICONS, platformIcon, platformLabel } from '../shared/icons/platform-icons';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import {
  HlmDialogDescription,
  HlmDialogHeader,
  HlmDialogTitle,
} from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { ServerService } from '../api/api/server.service';
import { ModpacksService } from '../api/api/modpacks.service';
import { ModpackSearchResult } from '../api/model/modpackSearchResult';

type DialogContext = { onCreated: () => void };

@Component({
  selector: 'app-server-create-dialog',
  imports: [
    NgIcon,
    HlmButtonImports,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
  ],
  providers: [provideIcons({ lucideSearch, lucideDownload, lucideCheck, simpleModrinth, simpleCurseforge, ...PLATFORM_ICONS })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Create Minecraft server</h3>
      <p hlmDialogDescription>
        CommandBlock provisions an <span class="font-mono">itzg/minecraft-server</span> container and
        routes players to it by hostname - no per-server port needed. Modpack types pull the server
        side of the pack on first boot.
      </p>
    </hlm-dialog-header>

    <div class="grid grid-cols-2 gap-3">
      <div class="flex flex-col gap-1.5">
        <label hlmLabel for="srv-type" class="text-muted-foreground text-xs uppercase tracking-wide">Type</label>
        <hlm-select [value]="serverType()" (valueChange)="serverType.set($event)">
          <hlm-select-trigger id="srv-type" class="w-full">
            <hlm-select-value placeholder="Pick a loader…" />
          </hlm-select-trigger>
          <hlm-select-content *hlmSelectPortal>
            @for (t of serverTypes; track t) {
              <hlm-select-item [value]="t">
                <span class="inline-flex items-center gap-2"><ng-icon [name]="icon(t)" size="15" /> {{ label(t) }}</span>
              </hlm-select-item>
            }
          </hlm-select-content>
        </hlm-select>
      </div>

      <div class="flex flex-col gap-1.5">
        <label hlmLabel for="srv-memory" class="text-muted-foreground text-xs uppercase tracking-wide">Memory</label>
        <input
          hlmInput
          id="srv-memory"
          placeholder="e.g. 4G"
          [value]="memory()"
          (input)="memory.set($any($event.target).value)"
        />
      </div>

      <div class="col-span-2 flex flex-col gap-1.5">
        <label hlmLabel for="srv-name" class="text-muted-foreground text-xs uppercase tracking-wide">Display name</label>
        <input
          hlmInput
          id="srv-name"
          placeholder="e.g. Survival SMP"
          [value]="displayName()"
          (input)="displayName.set($any($event.target).value)"
        />
      </div>

      <div class="col-span-2 flex flex-col gap-1.5">
        <label hlmLabel for="srv-host" class="text-muted-foreground text-xs uppercase tracking-wide">Hostname</label>
        <input
          hlmInput
          id="srv-host"
          placeholder="smp.gaggao.com"
          [value]="hostname()"
          (input)="hostname.set($any($event.target).value)"
        />
        <span class="text-muted-foreground text-xs">The address players connect with. Must be unique.</span>
      </div>

      @if (isModpack()) {
        <div class="col-span-2 flex flex-col gap-1.5">
          <label hlmLabel class="text-muted-foreground text-xs uppercase tracking-wide">Search Modrinth</label>
          <div class="flex gap-2">
            <input
              hlmInput
              class="flex-1"
              placeholder="e.g. cobblemon, create, all the mods…"
              [value]="modpackQuery()"
              (input)="modpackQuery.set($any($event.target).value)"
              (keydown.enter)="searchModpacks()"
            />
            <button hlmBtn variant="outline" type="button" (click)="searchModpacks()" [disabled]="searching()">
              <ng-icon name="lucideSearch" size="14" />
              {{ searching() ? 'Searching…' : 'Search' }}
            </button>
          </div>
          @if (searchError(); as se) {
            <p class="text-destructive text-xs">{{ se }}</p>
          }
          @if (results().length > 0) {
            <ul class="divide-border max-h-56 divide-y overflow-auto rounded-md border">
              @for (r of results(); track r.slug) {
                <li
                  class="hover:bg-accent flex cursor-pointer items-center gap-3 p-2"
                  [class.bg-accent]="modpackRef() === r.slug"
                  (click)="pickModpack(r)"
                >
                  @if (r.iconUrl) {
                    <img [src]="r.iconUrl" alt="" class="size-9 shrink-0 rounded" />
                  }
                  <div class="min-w-0 flex-1">
                    <div class="flex items-center gap-1.5">
                      <span class="truncate text-sm font-medium">{{ r.title }}</span>
                      @if (modpackRef() === r.slug) {
                        <ng-icon name="lucideCheck" size="14" class="text-primary shrink-0" />
                      }
                    </div>
                    <p class="text-muted-foreground truncate text-xs">{{ r.description }}</p>
                  </div>
                  <span class="text-muted-foreground shrink-0 font-mono text-[10px]">{{ r.slug }}</span>
                </li>
              }
            </ul>
          }
          <input
            hlmInput
            class="mt-1"
            placeholder="…or paste a slug / .mrpack URL"
            [value]="modpackRef()"
            (input)="modpackRef.set($any($event.target).value)"
          />
        </div>
      } @else {
        <div class="col-span-2 flex flex-col gap-1.5">
          <label hlmLabel for="srv-version" class="text-muted-foreground text-xs uppercase tracking-wide">Version</label>
          <input
            hlmInput
            id="srv-version"
            placeholder="e.g. 1.21.1 (blank = latest)"
            [value]="version()"
            (input)="version.set($any($event.target).value)"
          />
        </div>
      }
    </div>

    @if (error(); as err) {
      <p class="text-destructive text-sm">{{ err }}</p>
    }

    <div class="flex justify-end gap-2">
      <button hlmBtn variant="outline" type="button" (click)="close()" [disabled]="submitting()">Cancel</button>
      <button hlmBtn type="button" (click)="submit()" [disabled]="!canSubmit()">
        {{ submitting() ? 'Creating…' : 'Create server' }}
      </button>
    </div>
  `,
})
export class ServerCreateDialog {
  private readonly ref = inject<BrnDialogRef<unknown>>(BrnDialogRef);
  private readonly ctx = injectBrnDialogContext<DialogContext>();
  private readonly api = inject(ServerService);
  private readonly modpacksApi = inject(ModpacksService);

  protected readonly modpackQuery = signal('');
  protected readonly results = signal<ReadonlyArray<ModpackSearchResult>>([]);
  protected readonly searching = signal(false);
  protected readonly searchError = signal<string | null>(null);

  protected searchModpacks(): void {
    const q = this.modpackQuery().trim();
    if (q === '') return;
    this.searching.set(true);
    this.searchError.set(null);
    this.modpacksApi.apiModpacksGet(q).subscribe({
      next: (hits) => {
        this.results.set(hits);
        this.searching.set(false);
      },
      error: () => {
        this.searchError.set('Search failed - Modrinth may be unreachable.');
        this.searching.set(false);
      },
    });
  }

  protected pickModpack(r: ModpackSearchResult): void {
    this.modpackRef.set(r.slug);
  }

  protected icon(serverType: string): string {
    return platformIcon(serverType);
  }

  protected label(serverType: string): string {
    return platformLabel(serverType);
  }

  protected readonly serverTypes = [
    'VANILLA',
    'PAPER',
    'PURPUR',
    'FABRIC',
    'QUILT',
    'FORGE',
    'NEOFORGE',
    'SPIGOT',
    'MODRINTH',
  ] as const;

  private static readonly modpackTypes = new Set(['MODRINTH', 'CURSEFORGE', 'FTBA']);

  protected readonly serverType = signal<string | null>(null);
  protected readonly displayName = signal('');
  protected readonly hostname = signal('');
  protected readonly memory = signal('4G');
  protected readonly version = signal('');
  protected readonly modpackRef = signal('');
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly isModpack = computed(() => {
    const t = this.serverType();
    return t !== null && ServerCreateDialog.modpackTypes.has(t);
  });

  protected readonly canSubmit = computed(
    () =>
      !this.submitting() &&
      !!this.serverType() &&
      this.displayName().trim() !== '' &&
      this.hostname().trim() !== '' &&
      this.memory().trim() !== '' &&
      (!this.isModpack() || this.modpackRef().trim() !== ''),
  );

  protected submit(): void {
    if (!this.canSubmit()) return;
    this.submitting.set(true);
    this.error.set(null);

    const modpack = this.isModpack();
    this.api
      .apiServerPost({
        serverType: this.serverType()!,
        displayName: this.displayName().trim(),
        hostname: this.hostname().trim(),
        memory: this.memory().trim(),
        version: modpack || this.version().trim() === '' ? undefined : this.version().trim(),
        modpackRef: modpack ? this.modpackRef().trim() : undefined,
      })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.ctx.onCreated();
          this.ref.close();
        },
        error: (err: unknown) => {
          this.submitting.set(false);
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
  return 'Create failed';
}
