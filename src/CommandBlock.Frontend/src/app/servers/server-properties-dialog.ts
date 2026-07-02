import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { autoToHTML } from '@sfirew/minecraft-motd-parser';
import DOMPurify from 'dompurify';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { BrnSelectImports } from '@spartan-ng/brain/select';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';

type DialogContext = { server: ServerInstanceDto; onSaved?: () => void };

const SECTION = '§'; // Minecraft's section sign for colour/format codes

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
        <div class="flex flex-wrap items-center gap-1">
          @for (c of colors; track c.code) {
            <button type="button" class="border-border h-5 w-5 rounded border" [style.background]="c.hex"
              [title]="'§' + c.code" (click)="insert(c.code)"></button>
          }
          <span class="bg-border mx-1 h-4 w-px"></span>
          @for (f of formats; track f.code) {
            <button hlmBtn size="sm" variant="outline" type="button" class="h-6 px-2 text-xs"
              [class.font-bold]="f.code === 'l'" [class.italic]="f.code === 'o'" [class.underline]="f.code === 'n'"
              (click)="insert(f.code)">{{ f.label }}</button>
          }
        </div>
        <input #motdInput hlmInput [value]="motd()" (input)="motd.set($any($event.target).value)" placeholder="A Minecraft Server" />
        <div class="rounded-md border p-3 text-center font-mono text-sm leading-tight" style="background:#101014">
          <div class="whitespace-pre-wrap" [innerHTML]="motdHtml()"></div>
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
  private readonly sanitizer = inject(DomSanitizer);
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
    { code: 'l', label: 'B' }, { code: 'o', label: 'I' }, { code: 'n', label: 'U' }, { code: 'm', label: 'S' }, { code: 'r', label: 'Reset' },
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

  // The parser output is DOMPurify-sanitized before we trust it, so a MOTD carrying malicious HTML
  // (e.g. set via the file editor, then previewed here) can't run script - only safe styled spans survive.
  protected readonly motdHtml = computed<SafeHtml>(() =>
    this.sanitizer.bypassSecurityTrustHtml(DOMPurify.sanitize(autoToHTML(this.motd() || ''))),
  );

  constructor() {
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

  protected insert(code: string): void {
    const el = this.motdInput()?.nativeElement;
    const ins = SECTION + code;
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

function messageOf(err: unknown): string {
  if (err && typeof err === 'object' && 'error' in err) {
    const e = (err as { error: unknown }).error;
    if (e && typeof e === 'object' && 'error' in e) return String((e as { error: unknown }).error);
  }
  return err instanceof Error ? err.message : 'Save failed';
}
