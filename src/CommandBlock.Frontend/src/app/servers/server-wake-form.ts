import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { BrnSelectImports } from '@spartan-ng/brain/select';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';

/// The Wake & sleep section of the server-settings modal. Per-server power management saved straight to
/// the DB: wake-on-join is read live by the router, auto-sleep by the idle monitor (no restart/recreate).
@Component({
  selector: 'app-server-wake-form',
  imports: [HlmInputImports, HlmLabelImports, HlmCheckboxImports, HlmSelectImports, BrnSelectImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <p class="text-muted-foreground text-xs">
      Start this server automatically when a player joins while it's stopped, and stop it again once it
      sits idle. Both save instantly - the router and idle monitor read them live, no restart.
    </p>

    <label class="flex items-center gap-2 text-sm">
      <hlm-checkbox [checked]="wakeOnConnect()" (checkedChange)="setWake($event)" />
      <span class="text-foreground">Wake on join</span>
    </label>

    @if (wakeOnConnect()) {
      <div class="flex flex-col gap-1.5 pl-6">
        <label hlmLabel class="text-muted-foreground text-xs uppercase tracking-wide">When someone joins while it's stopped</label>
        <hlm-select [value]="wakeMode()" (valueChange)="setWakeMode($event ?? 'queue')">
          <hlm-select-trigger class="w-full max-w-sm"><hlm-select-value /></hlm-select-trigger>
          <hlm-select-content *hlmSelectPortal>
            <hlm-select-item value="queue">Hold them &amp; let them in automatically (experimental)</hlm-select-item>
            <hlm-select-item value="notify">Ask them to reconnect in a moment</hlm-select-item>
          </hlm-select-content>
        </hlm-select>

        @if (wakeMode() === 'queue') {
          <label hlmLabel for="wk-q" class="text-muted-foreground mt-1 text-xs uppercase tracking-wide">Max hold (seconds)</label>
          <input hlmInput id="wk-q" type="number" min="1" max="25" class="w-40" [value]="wakeQueue()" (change)="setQueue($any($event.target).value)" />
          <span class="text-muted-foreground text-xs">
            <span class="text-amber-500">Experimental</span> - hold the joining player and drop them straight in
            the moment the server is ready. If it isn't up within this window they're asked to reconnect. Capped at
            25s (under the client's ~30s login timeout); reliability depends on how fast the server boots.
          </span>
        } @else {
          <span class="text-muted-foreground text-xs">
            Immediately tell the player the server is starting; they reconnect once it's up.
          </span>
        }
      </div>
    }

    <div class="border-border border-t"></div>

    <label class="flex items-center gap-2 text-sm">
      <hlm-checkbox [checked]="autoSleep()" (checkedChange)="setAutoSleep($event)" />
      <span class="text-foreground">Auto-sleep when idle</span>
    </label>

    @if (autoSleep()) {
      <div class="flex flex-col gap-1.5 pl-6">
        <label hlmLabel for="sl-m" class="text-muted-foreground text-xs uppercase tracking-wide">Idle timeout (minutes)</label>
        <input hlmInput id="sl-m" type="number" min="1" max="1440" class="w-40" [value]="autoSleepMinutes()" (change)="setSleepMinutes($any($event.target).value)" />
        <span class="text-muted-foreground text-xs">
          Stop the server after this many minutes with no players online. Wake on join brings it back.
        </span>
      </div>
    }

    @if (saving()) {
      <span class="text-muted-foreground text-xs">saving…</span>
    } @else if (savedOk()) {
      <span class="text-primary text-xs">Saved</span>
    }
  `,
})
export class ServerWakeForm implements OnInit {
  readonly server = input.required<ServerInstanceDto>();
  private readonly api = inject(ServerService);

  protected readonly wakeOnConnect = signal(false);
  protected readonly wakeMode = signal<'queue' | 'notify'>('queue');
  protected readonly wakeQueue = signal(28);
  protected readonly autoSleep = signal(false);
  protected readonly autoSleepMinutes = signal(10);
  protected readonly saving = signal(false);
  protected readonly savedOk = signal(false);

  ngOnInit(): void {
    const s = this.server();
    this.wakeOnConnect.set(!!s.wakeOnConnect);
    const q = Number(s.wakeQueueSeconds ?? 0);
    this.wakeMode.set(q > 0 ? 'queue' : 'notify');
    this.wakeQueue.set(q > 0 ? q : 28); // default hold window when switching to the queue mode
    this.autoSleep.set(!!s.autoSleepEnabled);
    this.autoSleepMinutes.set(Number(s.autoSleepIdleMinutes ?? 10));
  }

  protected setWake(enabled: boolean): void {
    this.wakeOnConnect.set(enabled);
    this.save();
  }

  protected setWakeMode(mode: string): void {
    this.wakeMode.set(mode === 'notify' ? 'notify' : 'queue');
    this.save();
  }

  protected setQueue(value: string): void {
    this.wakeQueue.set(Math.max(1, Math.min(25, Math.floor(+value || 1))));
    this.save();
  }

  protected setAutoSleep(enabled: boolean): void {
    this.autoSleep.set(enabled);
    this.save();
  }

  protected setSleepMinutes(value: string): void {
    this.autoSleepMinutes.set(Math.max(1, Math.min(1440, Math.floor(+value || 1))));
    this.save();
  }

  private save(): void {
    this.saving.set(true);
    this.savedOk.set(false);
    this.api.apiServerIdWakePut(this.server().id, {
      wakeOnConnect: this.wakeOnConnect(),
      wakeQueueSeconds: this.wakeMode() === 'queue' ? this.wakeQueue() : 0,
      autoSleepEnabled: this.autoSleep(),
      autoSleepIdleMinutes: this.autoSleepMinutes(),
    }).subscribe({
      next: () => { this.saving.set(false); this.savedOk.set(true); },
      error: () => { this.saving.set(false); },
    });
  }
}
