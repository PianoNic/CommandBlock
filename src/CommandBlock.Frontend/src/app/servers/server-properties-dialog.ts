import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { interval } from 'rxjs';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { textToJSON } from '@sfirew/minecraft-motd-parser';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { BrnSelectImports } from '@spartan-ng/brain/select';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';
import { environment } from '../shared/environments/environment';

type DialogContext = { server: ServerInstanceDto; onSaved?: () => void };

const SECTION = '§'; // Minecraft's section sign for colour/format codes

interface MotdToken {
  text: string;
  color?: string;
  bold?: boolean;
  italic?: boolean;
  underlined?: boolean;
  strikethrough?: boolean;
  obfuscated?: boolean;
}

@Component({
  selector: 'app-server-properties-dialog',
  imports: [
    HlmButtonImports,
    HlmInputImports,
    HlmLabelImports,
    HlmCheckboxImports,
    HlmSelectImports,
    BrnSelectImports,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex max-h-[85svh] flex-col gap-4 overflow-y-auto' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Server settings - {{ ctx.server.displayName }}</h3>
      <p hlmDialogDescription>The most-used server.properties. Changes are written to the file and apply on the next restart.</p>
    </hlm-dialog-header>

    @if (loading()) {
      <p class="text-muted-foreground text-sm">Loading…</p>
    } @else if (!available()) {
      <p class="text-muted-foreground text-sm">
        server.properties hasn't been generated yet. Start the server once, then come back to edit it.
      </p>
    } @else {
      <!-- MOTD editor + live preview -->
      <div class="flex flex-col gap-1.5">
        <label hlmLabel class="text-muted-foreground text-xs uppercase tracking-wide">MOTD</label>

        <!-- Colour swatches -->
        <div class="flex flex-wrap items-center gap-1">
          @for (c of colors; track c.code) {
            <button type="button" class="border-border h-5 w-5 rounded border" [style.background]="c.hex"
              [title]="'§' + c.code" (click)="insert(c.code)"></button>
          }
        </div>
        <!-- Formatting on its own row -->
        <div class="flex flex-wrap items-center gap-1">
          @for (f of formats; track f.code) {
            <button hlmBtn size="sm" variant="outline" type="button" class="h-6 px-2 text-xs"
              [class.font-bold]="f.code === 'l'" [class.italic]="f.code === 'o'" [class.underline]="f.code === 'n'" [class.line-through]="f.code === 'm'"
              [title]="f.title" (click)="insert(f.code)">{{ f.label }}</button>
          }
          <button hlmBtn size="sm" variant="outline" type="button" class="h-6 px-2 text-xs" title="Insert a line break (\\n) - MOTD can be two lines" (click)="insertNewline()">↵ Line</button>
        </div>

        <input #motdInput hlmInput [value]="motd()" (input)="motd.set($any($event.target).value)" placeholder="A Minecraft Server" />

        <!-- Server-list-style preview: icon + MOTD (top-aligned + wrapping, like the in-game list) -->
        <div class="flex items-start gap-3 rounded-md border p-2" style="background:#0d0d12">
          <img [src]="previewIconUrl()" alt="" class="h-16 w-16 shrink-0 rounded-sm" style="image-rendering:pixelated" />
          <div class="min-w-0 flex-1 overflow-hidden"
            style="font-family:'Minecraft',monospace; font-size:15px; line-height:1.4; color:#FFFFFF; white-space:pre-wrap; word-break:break-word">
            @for (t of tokens(); track $index) {
              <span [style.color]="t.color || '#FFFFFF'" [style.font-weight]="t.bold ? 'bold' : null"
                [style.font-style]="t.italic ? 'italic' : null" [style.text-decoration]="decoration(t)">{{ t.obfuscated ? scramble(t) : t.text }}</span>
            } @empty {
              <span style="color:#6b6b6b">A Minecraft Server</span>
            }
          </div>
        </div>
      </div>

      <div class="grid grid-cols-2 gap-3">
        <div class="flex flex-col gap-1.5">
          <label hlmLabel class="text-muted-foreground text-xs uppercase tracking-wide">Difficulty</label>
          <hlm-select [value]="difficulty()" (valueChange)="difficulty.set($event ?? 'easy')">
            <hlm-select-trigger class="w-full"><hlm-select-value /></hlm-select-trigger>
            <hlm-select-content *hlmSelectPortal>
              @for (d of difficulties; track d) { <hlm-select-item [value]="d">{{ d }}</hlm-select-item> }
            </hlm-select-content>
          </hlm-select>
        </div>
        <div class="flex flex-col gap-1.5">
          <label hlmLabel class="text-muted-foreground text-xs uppercase tracking-wide">Gamemode</label>
          <hlm-select [value]="gamemode()" (valueChange)="gamemode.set($event ?? 'survival')">
            <hlm-select-trigger class="w-full"><hlm-select-value /></hlm-select-trigger>
            <hlm-select-content *hlmSelectPortal>
              @for (g of gamemodes; track g) { <hlm-select-item [value]="g">{{ g }}</hlm-select-item> }
            </hlm-select-content>
          </hlm-select>
        </div>
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="p-max" class="text-muted-foreground text-xs uppercase tracking-wide">Max players</label>
          <input hlmInput id="p-max" type="number" min="1" [value]="maxPlayers()" (input)="maxPlayers.set(+$any($event.target).value)" />
        </div>
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="p-view" class="text-muted-foreground text-xs uppercase tracking-wide">View distance</label>
          <input hlmInput id="p-view" type="number" min="2" max="32" [value]="viewDistance()" (input)="viewDistance.set(+$any($event.target).value)" />
        </div>
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="p-spawn" class="text-muted-foreground text-xs uppercase tracking-wide">Spawn protection</label>
          <input hlmInput id="p-spawn" type="number" min="0" [value]="spawnProtection()" (input)="spawnProtection.set(+$any($event.target).value)" />
        </div>
      </div>

      <div class="grid grid-cols-2 gap-x-4 gap-y-2">
        <label class="flex items-center gap-2 text-sm"><hlm-checkbox [checked]="pvp()" (checkedChange)="pvp.set($event)" /> PVP</label>
        <label class="flex items-center gap-2 text-sm"><hlm-checkbox [checked]="whitelist()" (checkedChange)="whitelist.set($event)" /> Whitelist</label>
        <label class="flex items-center gap-2 text-sm"><hlm-checkbox [checked]="hardcore()" (checkedChange)="hardcore.set($event)" /> Hardcore</label>
        <label class="flex items-center gap-2 text-sm"><hlm-checkbox [checked]="allowFlight()" (checkedChange)="allowFlight.set($event)" /> Allow flight</label>
        <label class="flex items-center gap-2 text-sm"><hlm-checkbox [checked]="enableCommandBlock()" (checkedChange)="enableCommandBlock.set($event)" /> Command blocks</label>
        <label class="flex items-center gap-2 text-sm"><hlm-checkbox [checked]="onlineMode()" (checkedChange)="onlineMode.set($event)" /> Online mode</label>
      </div>

      @if (error(); as e) { <p class="text-destructive text-sm">{{ e }}</p> }

      <div class="flex justify-end gap-2">
        <button hlmBtn variant="outline" size="sm" type="button" (click)="close()">Cancel</button>
        <button hlmBtn size="sm" type="button" (click)="save()" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Save' }}</button>
      </div>
    }
  `,
})
export class ServerPropertiesDialog {
  protected readonly ctx = injectBrnDialogContext<DialogContext>();
  private readonly api = inject(ServerService);
  private readonly ref = inject<BrnDialogRef<unknown>>(BrnDialogRef);

  private readonly motdInput = viewChild<ElementRef<HTMLInputElement>>('motdInput');

  protected readonly loading = signal(true);
  protected readonly available = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly difficulties = ['peaceful', 'easy', 'normal', 'hard'] as const;
  protected readonly gamemodes = ['survival', 'creative', 'adventure', 'spectator'] as const;
  protected readonly colors = [
    { code: '0', hex: '#000000' }, { code: '1', hex: '#0000AA' }, { code: '2', hex: '#00AA00' }, { code: '3', hex: '#00AAAA' },
    { code: '4', hex: '#AA0000' }, { code: '5', hex: '#AA00AA' }, { code: '6', hex: '#FFAA00' }, { code: '7', hex: '#AAAAAA' },
    { code: '8', hex: '#555555' }, { code: '9', hex: '#5555FF' }, { code: 'a', hex: '#55FF55' }, { code: 'b', hex: '#55FFFF' },
    { code: 'c', hex: '#FF5555' }, { code: 'd', hex: '#FF55FF' }, { code: 'e', hex: '#FFFF55' }, { code: 'f', hex: '#FFFFFF' },
  ];
  protected readonly formats = [
    { code: 'l', label: 'B', title: 'Bold (§l)' },
    { code: 'o', label: 'I', title: 'Italic (§o)' },
    { code: 'n', label: 'U', title: 'Underline (§n)' },
    { code: 'm', label: 'S', title: 'Strikethrough (§m)' },
    { code: 'k', label: 'obf', title: 'Obfuscated / scramble (§k)' },
    { code: 'r', label: 'Reset', title: 'Reset formatting (§r)' },
  ];

  protected readonly motd = signal('');
  protected readonly maxPlayers = signal(20);
  protected readonly difficulty = signal<string>('easy');
  protected readonly gamemode = signal<string>('survival');
  protected readonly pvp = signal(true);
  protected readonly onlineMode = signal(true);
  protected readonly whitelist = signal(false);
  protected readonly hardcore = signal(false);
  protected readonly allowFlight = signal(false);
  protected readonly enableCommandBlock = signal(false);
  protected readonly viewDistance = signal(10);
  protected readonly spawnProtection = signal(16);

  // The MOTD rendered as styled tokens (safe: Angular escapes each token's text on interpolation).
  // server.properties stores a line break as the literal "\n" escape - expand it so the preview wraps.
  protected readonly tokens = computed<MotdToken[]>(() =>
    flattenMotd(textToJSON((this.motd() || '').replace(/\\n/g, '\n'))),
  );
  // Drives the obfuscated-text scramble animation.
  private readonly tick = signal(0);
  private readonly scrambleChars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789?!@#%&';

  constructor() {
    // Fast ticker for the §k scramble effect; only causes re-renders while an obfuscated token exists.
    interval(60).pipe(takeUntilDestroyed()).subscribe(() => this.tick.update((v) => v + 1));

    this.api.apiServerIdPropertiesGet(this.ctx.server.id).subscribe({
      next: (p) => {
        this.available.set(p.available);
        if (p.available) {
          this.motd.set(p.motd ?? '');
          this.maxPlayers.set(Number(p.maxPlayers ?? 20));
          this.difficulty.set(p.difficulty ?? 'easy');
          this.gamemode.set(p.gamemode ?? 'survival');
          this.pvp.set(!!p.pvp);
          this.onlineMode.set(!!p.onlineMode);
          this.whitelist.set(!!p.whitelist);
          this.hardcore.set(!!p.hardcore);
          this.allowFlight.set(!!p.allowFlight);
          this.enableCommandBlock.set(!!p.enableCommandBlock);
          this.viewDistance.set(Number(p.viewDistance ?? 10));
          this.spawnProtection.set(Number(p.spawnProtection ?? 16));
        }
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  protected decoration(t: MotdToken): string | null {
    const parts: string[] = [];
    if (t.underlined) parts.push('underline');
    if (t.strikethrough) parts.push('line-through');
    return parts.length ? parts.join(' ') : null;
  }

  protected scramble(t: MotdToken): string {
    this.tick(); // establish a dependency so this re-runs each animation tick
    let out = '';
    for (const ch of t.text) out += ch === ' ' || ch === '\n' ? ch : this.scrambleChars[Math.floor(Math.random() * this.scrambleChars.length)];
    return out;
  }

  protected previewIconUrl(): string {
    const s = this.ctx.server;
    return s.hasIcon ? `${environment.apiBaseUrl}/api/Server/${s.id}/icon` : 'default-server-icon.png';
  }

  protected insert(code: string): void {
    this.insertAtCursor(SECTION + code);
  }

  /// Inserts the literal "\n" escape - server.properties stores line breaks that way, and MC renders
  /// the MOTD as (up to) two lines. The preview expands it to a real break.
  protected insertNewline(): void {
    this.insertAtCursor('\\n');
  }

  private insertAtCursor(ins: string): void {
    const el = this.motdInput()?.nativeElement;
    if (el) {
      const start = el.selectionStart ?? el.value.length;
      const end = el.selectionEnd ?? start;
      const v = this.motd();
      this.motd.set(v.slice(0, start) + ins + v.slice(end));
      queueMicrotask(() => { el.focus(); const pos = start + ins.length; el.setSelectionRange(pos, pos); });
    } else {
      this.motd.set(this.motd() + ins);
    }
  }

  protected save(): void {
    this.saving.set(true);
    this.error.set(null);
    this.api.apiServerIdPropertiesPut(this.ctx.server.id, {
      motd: this.motd(),
      maxPlayers: this.maxPlayers(),
      difficulty: this.difficulty(),
      gamemode: this.gamemode(),
      pvp: this.pvp(),
      onlineMode: this.onlineMode(),
      whitelist: this.whitelist(),
      hardcore: this.hardcore(),
      allowFlight: this.allowFlight(),
      enableCommandBlock: this.enableCommandBlock(),
      viewDistance: this.viewDistance(),
      spawnProtection: this.spawnProtection(),
    }).subscribe({
      next: () => { this.saving.set(false); this.ctx.onSaved?.(); this.close(); },
      error: (err: unknown) => { this.saving.set(false); this.error.set(messageOf(err)); },
    });
  }

  protected close(): void {
    this.ref.close();
  }
}

/// Flattens the parser's Minecraft text-component tree into a flat list of styled tokens, with each
/// child inheriting its parent's formatting (standard Minecraft JSON text semantics).
function flattenMotd(node: unknown, inherited: Partial<MotdToken> = {}): MotdToken[] {
  if (!node || typeof node !== 'object') return [];
  const n = node as Record<string, unknown>;
  const props: Partial<MotdToken> = {
    color: (n['color'] as string) ?? inherited.color,
    bold: (n['bold'] as boolean) ?? inherited.bold,
    italic: (n['italic'] as boolean) ?? inherited.italic,
    underlined: (n['underlined'] as boolean) ?? inherited.underlined,
    strikethrough: (n['strikethrough'] as boolean) ?? inherited.strikethrough,
    obfuscated: (n['obfuscated'] as boolean) ?? inherited.obfuscated,
  };
  const out: MotdToken[] = [];
  if (typeof n['text'] === 'string' && (n['text'] as string).length > 0) out.push({ text: n['text'] as string, ...props });
  for (const child of (n['extra'] as unknown[]) ?? []) out.push(...flattenMotd(child, props));
  return out;
}

function messageOf(err: unknown): string {
  if (err && typeof err === 'object' && 'error' in err) {
    const e = (err as { error: unknown }).error;
    if (e && typeof e === 'object' && 'error' in e) return String((e as { error: unknown }).error);
  }
  return err instanceof Error ? err.message : 'Save failed';
}
