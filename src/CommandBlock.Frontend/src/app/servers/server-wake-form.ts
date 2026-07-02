import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';

/// The Wake-on-join section of the server-settings modal. A per-server setting saved straight to the
/// DB and read live by the router (no restart/recreate).
@Component({
  selector: 'app-server-wake-form',
  imports: [HlmInputImports, HlmLabelImports, HlmCheckboxImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <p class="text-muted-foreground text-xs">
      Start this server automatically when a player connects while it's stopped. Saved instantly - the
      router reads it live, no restart.
    </p>

    <label class="flex items-center gap-2 text-sm">
      <hlm-checkbox [checked]="wakeOnConnect()" (checkedChange)="setWake($event)" />
      <span class="text-foreground">Wake on join</span>
    </label>

    @if (wakeOnConnect()) {
      <div class="flex flex-col gap-1.5">
        <label hlmLabel for="wk-q" class="text-muted-foreground text-xs uppercase tracking-wide">Join queue (seconds)</label>
        <input hlmInput id="wk-q" type="number" min="0" max="28" class="w-40" [value]="wakeQueue()" (change)="setQueue($any($event.target).value)" />
        <span class="text-muted-foreground text-xs">
          Hold the joining player and let them straight in the moment the server is ready (up to 28s).
          0 = ask them to reconnect once it's booting.
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
  protected readonly wakeQueue = signal(0);
  protected readonly saving = signal(false);
  protected readonly savedOk = signal(false);

  ngOnInit(): void {
    const s = this.server();
    this.wakeOnConnect.set(!!s.wakeOnConnect);
    this.wakeQueue.set(Number(s.wakeQueueSeconds ?? 0));
  }

  protected setWake(enabled: boolean): void {
    this.wakeOnConnect.set(enabled);
    this.save();
  }

  protected setQueue(value: string): void {
    this.wakeQueue.set(Math.max(0, Math.min(28, Math.floor(+value || 0))));
    this.save();
  }

  private save(): void {
    this.saving.set(true);
    this.savedOk.set(false);
    this.api.apiServerIdWakePut(this.server().id, {
      wakeOnConnect: this.wakeOnConnect(),
      wakeQueueSeconds: this.wakeQueue(),
    }).subscribe({
      next: () => { this.saving.set(false); this.savedOk.set(true); },
      error: () => { this.saving.set(false); },
    });
  }
}
