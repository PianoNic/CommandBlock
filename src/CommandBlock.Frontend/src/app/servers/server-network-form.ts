import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';

/// The Network section of the server-settings modal: whether the server answers on the shared router
/// port by hostname, on its own host port, or both. Publishing a port is baked into the container at
/// create time, so saving that recreates the container - hence an explicit Save rather than the
/// write-through the wake tab uses.
@Component({
  selector: 'app-server-network-form',
  imports: [HlmInputImports, HlmLabelImports, HlmCheckboxImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <p class="text-muted-foreground text-xs">
      By default a server has no port of its own - players reach it through the router on
      <span class="font-mono">{{ server().hostname }}</span
      >, and the host keeps a single open game port. Publish a port to reach it directly instead, e.g. on a
      LAN where you'd rather not set up DNS.
    </p>

    <label class="flex items-center gap-2 text-sm">
      <hlm-checkbox aria-label="Reachable through the router by hostname" [checked]="routed()" (checkedChange)="routed.set($event)" />
      <span class="text-foreground">Reachable through the router by hostname</span>
    </label>

    <div class="border-border border-t"></div>

    <label class="flex items-center gap-2 text-sm">
      <hlm-checkbox aria-label="Publish a port on the host" [checked]="publish()" (checkedChange)="setPublish($event)" />
      <span class="text-foreground">Publish a port on the host</span>
    </label>

    @if (publish()) {
      <div class="flex flex-col gap-3 pl-6">
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="net-port" class="text-muted-foreground text-xs uppercase tracking-wide">Host port</label>
          <input
            hlmInput
            id="net-port"
            type="number"
            min="1"
            max="65535"
            class="w-40"
            [value]="port()"
            (input)="port.set(+$any($event.target).value)"
          />
        </div>

        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="net-bind" class="text-muted-foreground text-xs uppercase tracking-wide">
            Bind to address (optional)
          </label>
          <input
            hlmInput
            id="net-bind"
            class="w-64 font-mono"
            placeholder="all interfaces"
            [value]="bind()"
            (input)="bind.set($any($event.target).value)"
          />
          <span class="text-muted-foreground text-xs">
            Leave empty to listen on every interface. Set your machine's LAN address (e.g.
            <span class="font-mono">192.168.1.50</span>) to keep the server off a public interface, or
            <span class="font-mono">127.0.0.1</span> for this machine only.
          </span>
        </div>

        @if (bind().trim() === '') {
          <p class="text-xs text-amber-600 dark:text-amber-500">
            On an internet-facing host this port is reachable from anywhere the firewall allows - it does not go
            through the router. Bind it to a private address to keep it on the LAN.
          </p>
        }
      </div>
    }

    @if (unreachable()) {
      <p class="text-destructive text-xs">
        With both off, nothing can reach this server. Keep it on the router or give it a port.
      </p>
    }

    @if (error()) {
      <p class="text-destructive text-xs">{{ error() }}</p>
    } @else if (saving()) {
      <span class="text-muted-foreground text-xs">saving…</span>
    } @else if (savedOk()) {
      <span class="text-primary text-xs">Saved</span>
    }
  `,
})
export class ServerNetworkForm implements OnInit {
  readonly server = input.required<ServerInstanceDto>();
  readonly saved = output<void>();
  private readonly api = inject(ServerService);

  protected readonly routed = signal(true);
  protected readonly publish = signal(false);
  protected readonly port = signal(25566);
  protected readonly bind = signal('');
  protected readonly saving = signal(false);
  protected readonly savedOk = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly unreachable = computed(() => !this.routed() && !this.publish());

  ngOnInit(): void {
    const s = this.server();
    this.routed.set(s.routedThroughProxy !== false);
    const p = s.lanPort ?? null;
    this.publish.set(p !== null);
    if (p !== null) this.port.set(Number(p));
    this.bind.set(s.lanBindAddress ?? '');
  }

  protected setPublish(on: boolean): void {
    this.publish.set(on);
    // A server taken off the router needs somewhere to go, so offer the standard port straight away.
    if (on && !this.port()) this.port.set(25566);
  }

  /// Called by the dialog's pinned Save button.
  save(): void {
    if (this.unreachable()) return;

    this.saving.set(true);
    this.savedOk.set(false);
    this.error.set(null);

    this.api
      .apiServerIdNetworkPut(this.server().id, {
        lanPort: this.publish() ? Math.floor(this.port()) : null,
        lanBindAddress: this.publish() ? this.bind().trim() : null,
        routedThroughProxy: this.routed(),
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.savedOk.set(true);
          this.saved.emit();
        },
        error: (err: { error?: { error?: string } }) => {
          this.saving.set(false);
          this.error.set(err?.error?.error ?? 'Could not apply the network settings.');
        },
      });
  }
}
