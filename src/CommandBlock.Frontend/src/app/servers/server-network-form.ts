import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';

/// The Network section of the server-settings modal. A server is reached either through the router by
/// hostname or directly on a host port of its own - never both, so this is a choice rather than two
/// switches. Publishing is baked into the container at create time, so switching mode recreates it;
/// hence an explicit Save rather than the write-through the wake tab uses.
@Component({
  selector: 'app-server-network-form',
  imports: [HlmInputImports, HlmLabelImports, HlmRadioGroupImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <p class="text-muted-foreground text-xs">How players reach this server. Changing it restarts the server.</p>

    <hlm-radio-group [value]="mode()" (valueChange)="mode.set($any($event))" class="flex flex-col gap-4">
      <label class="flex items-start gap-2.5 text-sm">
        <hlm-radio value="router" aria-label="Through the router by hostname" class="mt-0.5">
          <hlm-radio-indicator indicator />
        </hlm-radio>
        <span class="flex flex-col gap-0.5">
          <span class="text-foreground">Through the router by hostname</span>
          <span class="text-muted-foreground text-xs">
            Shares the one open game port with every other routed server. No extra port on the host.
          </span>
        </span>
      </label>

      @if (mode() === 'router') {
        <div class="flex flex-col gap-1.5 pl-6">
          @if (server().hostname) {
            <span class="text-muted-foreground text-xs">
              Players connect to <span class="text-foreground font-mono">{{ server().hostname }}</span
              >. Rename it under General.
            </span>
          } @else {
            <!-- Created as a direct server, so there's no hostname yet - it has to be set to move onto the router. -->
            <label hlmLabel for="net-host" class="text-muted-foreground text-xs uppercase tracking-wide">Hostname</label>
            <input
              hlmInput
              id="net-host"
              class="w-72 font-mono"
              placeholder="smp.example.com"
              [value]="hostname()"
              (input)="hostname.set($any($event.target).value)"
            />
            <span class="text-muted-foreground text-xs">The address players type. Must be unique.</span>
          }
        </div>
      }

      <label class="flex items-start gap-2.5 text-sm">
        <hlm-radio value="direct" aria-label="Directly on a host port" class="mt-0.5">
          <hlm-radio-indicator indicator />
        </hlm-radio>
        <span class="flex flex-col gap-0.5">
          <span class="text-foreground">Directly on a host port</span>
          <span class="text-muted-foreground text-xs">
            Reached as <span class="font-mono">&lt;host-ip&gt;:&lt;port&gt;</span>, with no DNS to set up. The router
            stops answering for this server.
          </span>
        </span>
      </label>

      @if (mode() === 'direct') {
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
              On an internet-facing host this port is reachable from anywhere the firewall allows. Bind it to a
              private address to keep it on the LAN.
            </p>
          }
        </div>
      }
    </hlm-radio-group>

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

  protected readonly mode = signal<'router' | 'direct'>('router');
  protected readonly hostname = signal('');
  protected readonly port = signal(25566);
  protected readonly bind = signal('');
  protected readonly saving = signal(false);
  protected readonly savedOk = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly needsHostname = computed(() => this.mode() === 'router' && !this.server().hostname);

  ngOnInit(): void {
    const s = this.server();
    this.mode.set(s.routedThroughProxy === false ? 'direct' : 'router');
    this.hostname.set(s.hostname ?? '');
    if (s.lanPort) this.port.set(Number(s.lanPort));
    this.bind.set(s.lanBindAddress ?? '');
  }

  /// Called by the dialog's pinned Save button.
  save(): void {
    const routed = this.mode() === 'router';

    if (routed && this.needsHostname() && this.hostname().trim() === '') {
      this.error.set('A hostname is required to reach this server through the router.');
      return;
    }
    if (!routed && !this.port()) {
      this.error.set('A port is required to reach this server directly.');
      return;
    }

    this.saving.set(true);
    this.savedOk.set(false);
    this.error.set(null);

    this.api
      .apiServerIdNetworkPut(this.server().id, {
        routedThroughProxy: routed,
        lanPort: routed ? null : Math.floor(this.port()),
        lanBindAddress: routed ? null : this.bind().trim(),
        hostname: routed ? this.hostname().trim() || null : null,
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
