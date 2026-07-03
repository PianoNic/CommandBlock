import { ChangeDetectionStrategy, Component, ElementRef, OnInit, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { interval } from 'rxjs';
import { textToJSON } from '@sfirew/minecraft-motd-parser';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { BrnSelectImports } from '@spartan-ng/brain/select';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';
import { DomainsService } from '../api/api/domains.service';
import { DomainDto } from '../api/model/domainDto';
import { environment } from '../shared/environments/environment';

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

/// The General / MOTD section of the server-settings modal: the most-used server.properties plus a
/// live MOTD editor. Standalone form (no dialog chrome) so it can sit inside a tabbed modal.
@Component({
  selector: 'app-server-properties-form',
  imports: [HlmButtonImports, HlmInputImports, HlmLabelImports, HlmCheckboxImports, HlmSelectImports, BrnSelectImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <div class="flex flex-col gap-1.5">
      <label hlmLabel for="disp-name" class="text-muted-foreground text-xs uppercase tracking-wide">Display name</label>
      <input hlmInput id="disp-name" class="max-w-sm" [value]="displayName()" (input)="displayName.set($any($event.target).value)" (change)="saveIdentity()" placeholder="My server" />
    </div>

    <div class="flex flex-col gap-1.5">
      <label hlmLabel for="disp-sub" class="text-muted-foreground text-xs uppercase tracking-wide">Hostname</label>
      <div class="flex items-center gap-2">
        <input hlmInput id="disp-sub" class="min-w-0 flex-1" placeholder="smp" [value]="subdomain()" (input)="subdomain.set($any($event.target).value)" (change)="saveIdentity()" />
        <span class="text-muted-foreground">.</span>
        <hlm-select [value]="domain()" (valueChange)="domain.set($event ?? ''); saveIdentity()">
          <hlm-select-trigger class="w-44"><hlm-select-value placeholder="domain" /></hlm-select-trigger>
          <hlm-select-content *hlmSelectPortal>
            @for (d of domainNames(); track d) {
              <hlm-select-item [value]="d">{{ d }}</hlm-select-item>
            }
          </hlm-select-content>
        </hlm-select>
      </div>
    </div>

    <div class="flex items-center gap-2 text-xs">
      @if (savingIdentity()) { <span class="text-muted-foreground">saving…</span> }
      @else if (identitySaved()) { <span class="text-primary">Saved</span> }
      @if (identityError(); as e) { <span class="text-destructive">{{ e }}</span> }
      <span class="text-muted-foreground">Players connect to <span class="text-foreground font-mono">{{ fullHostname() }}</span>; changing it reroutes on the next join.</span>
    </div>

    <p class="text-muted-foreground text-xs">The most-used server.properties. Changes are written to the file and apply on the next restart.</p>

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
        <div class="flex flex-wrap items-center gap-1">
          @for (c of colors; track c.code) {
            <button type="button" class="border-border h-5 w-5 rounded border" [style.background]="c.hex"
              [title]="'§' + c.code" (click)="insert(c.code)"></button>
          }
        </div>
        <div class="flex flex-wrap items-center gap-1">
          @for (f of formats; track f.code) {
            <button hlmBtn size="sm" variant="outline" type="button" class="h-6 px-2 text-xs"
              [class.font-bold]="f.code === 'l'" [class.italic]="f.code === 'o'" [class.underline]="f.code === 'n'" [class.line-through]="f.code === 'm'"
              [title]="f.title" (click)="insert(f.code)">{{ f.label }}</button>
          }
          <button hlmBtn size="sm" variant="outline" type="button" class="h-6 px-2 text-xs" title="Insert a line break (\\n) - MOTD can be two lines" (click)="insertNewline()">↵ Line</button>
        </div>

        <input #motdInput hlmInput [value]="motd()" (input)="motd.set($any($event.target).value)" placeholder="A Minecraft Server" />

        <div class="flex items-start gap-3 rounded-md border p-2" style="background:#0d0d12">
          <img [src]="previewIconUrl()" alt="" class="h-16 w-16 shrink-0 rounded-md" style="image-rendering:pixelated" />
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

      <div class="flex items-center justify-end gap-2">
        @if (savedOk()) { <span class="text-primary text-xs">Saved</span> }
        <button hlmBtn size="sm" type="button" (click)="save()" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Save' }}</button>
      </div>
    }
  `,
})
export class ServerPropertiesForm implements OnInit {
  readonly server = input.required<ServerInstanceDto>();
  readonly saved = output<void>();

  private readonly api = inject(ServerService);
  private readonly domainsApi = inject(DomainsService);
  private readonly motdInput = viewChild<ElementRef<HTMLInputElement>>('motdInput');

  protected readonly loading = signal(true);
  protected readonly available = signal(false);
  protected readonly saving = signal(false);
  protected readonly savedOk = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly displayName = signal('');
  protected readonly subdomain = signal('');
  protected readonly domain = signal('');
  protected readonly domains = signal<ReadonlyArray<DomainDto>>([]);
  protected readonly savingIdentity = signal(false);
  protected readonly identitySaved = signal(false);
  protected readonly identityError = signal<string | null>(null);

  // The domain select always offers the server's current domain even if it's not (or no longer) in
  // Settings -> Domains, so editing never drops the existing value.
  protected readonly domainNames = computed(() => {
    const names = this.domains().map((d) => d.name);
    const cur = this.domain();
    return cur && !names.includes(cur) ? [cur, ...names] : names;
  });

  // Composed like the create dialog: <subdomain>.<domain> (or just the domain for a root server).
  protected readonly fullHostname = computed(() => {
    const sub = this.subdomain().trim().toLowerCase().replace(/\.+$/, '');
    const dom = this.domain();
    return sub && dom ? `${sub}.${dom}` : dom || sub;
  });

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

  protected readonly tokens = computed<MotdToken[]>(() =>
    flattenMotd(textToJSON((this.motd() || '').replace(/\\n/g, '\n'))),
  );
  private readonly tick = signal(0);
  private readonly scrambleChars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789?!@#%&';

  constructor() {
    interval(60).pipe(takeUntilDestroyed()).subscribe(() => this.tick.update((v) => v + 1));
  }

  ngOnInit(): void {
    this.displayName.set(this.server().displayName ?? '');
    this.loadDomainsAndSplitHostname();
    this.api.apiServerIdPropertiesGet(this.server().id).subscribe({
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
    this.tick();
    let out = '';
    for (const ch of t.text) out += ch === ' ' || ch === '\n' ? ch : this.scrambleChars[Math.floor(Math.random() * this.scrambleChars.length)];
    return out;
  }

  protected previewIconUrl(): string {
    const s = this.server();
    return s.hasIcon ? `${environment.apiBaseUrl}/api/Server/${s.id}/icon` : 'default-server-icon.png';
  }

  protected insert(code: string): void {
    this.insertAtCursor(SECTION + code);
  }

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
    this.savedOk.set(false);
    this.error.set(null);
    this.api.apiServerIdPropertiesPut(this.server().id, {
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
      next: () => { this.saving.set(false); this.savedOk.set(true); this.saved.emit(); },
      error: (err: unknown) => { this.saving.set(false); this.error.set(messageOf(err)); },
    });
  }

  // Split the current hostname into subdomain + domain, preferring the longest configured domain that
  // is a suffix of it (falling back to a first-dot split until domains load).
  private loadDomainsAndSplitHostname(): void {
    const host = (this.server().hostname ?? '').toLowerCase();
    const dot = host.indexOf('.');
    this.subdomain.set(dot > 0 ? host.slice(0, dot) : host);
    this.domain.set(dot > 0 ? host.slice(dot + 1) : '');

    this.domainsApi.apiDomainsGet().subscribe({
      next: (d) => {
        this.domains.set(d);
        const match = d
          .map((x) => x.name.toLowerCase())
          .filter((name) => host === name || host.endsWith('.' + name))
          .sort((a, b) => b.length - a.length)[0];
        if (match) {
          this.subdomain.set(host === match ? '' : host.slice(0, host.length - match.length - 1));
          this.domain.set(match);
        }
      },
    });
  }

  protected saveIdentity(): void {
    const name = this.displayName().trim();
    const host = this.fullHostname().trim().toLowerCase();
    if (!name || !host) return;
    if (name === (this.server().displayName ?? '') && host === (this.server().hostname ?? '')) return;
    this.savingIdentity.set(true);
    this.identitySaved.set(false);
    this.identityError.set(null);
    this.api.apiServerIdNamePut(this.server().id, { displayName: name, hostname: host }).subscribe({
      next: () => { this.savingIdentity.set(false); this.identitySaved.set(true); this.saved.emit(); },
      error: (err: unknown) => { this.savingIdentity.set(false); this.identityError.set(messageOf(err)); },
    });
  }
}

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
