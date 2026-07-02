import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { DomainsService } from '../api/api/domains.service';

type DialogContext = { onAdded: () => void };

@Component({
  selector: 'app-domain-add-dialog',
  imports: [HlmButtonImports, HlmDialogHeader, HlmDialogTitle, HlmDialogDescription, HlmInputImports, HlmLabelImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Add a domain</h3>
      <p hlmDialogDescription>
        One-time DNS setup so players can reach your servers. Once a domain is added, new servers
        just pick a subdomain under it.
      </p>
    </hlm-dialog-header>

    <ol class="flex flex-col gap-3 text-sm">
      <li class="flex gap-3">
        <span class="bg-primary/15 text-primary flex size-6 shrink-0 items-center justify-center rounded-full text-xs font-semibold">1</span>
        <div>
          <p class="font-medium">Point a wildcard at this server</p>
          <p class="text-muted-foreground text-xs">
            At your DNS provider add an <span class="font-mono">A</span> record -
            name <span class="font-mono">*.yourdomain</span>, value = this server's public IP.
            Every subdomain then resolves here, so you never touch DNS again per server.
          </p>
        </div>
      </li>
      <li class="flex gap-3">
        <span class="bg-primary/15 text-primary flex size-6 shrink-0 items-center justify-center rounded-full text-xs font-semibold">2</span>
        <div>
          <p class="font-medium">On Cloudflare, turn the proxy off</p>
          <p class="text-muted-foreground text-xs">
            Set the record to <span class="font-mono">DNS only</span> (grey cloud). Minecraft isn't
            HTTP, so the orange-cloud proxy would block the connection.
          </p>
        </div>
      </li>
      <li class="flex gap-3">
        <span class="bg-primary/15 text-primary flex size-6 shrink-0 items-center justify-center rounded-full text-xs font-semibold">3</span>
        <div>
          <p class="font-medium">Open one port</p>
          <p class="text-muted-foreground text-xs">
            Allow inbound TCP <span class="font-mono">25565</span> in your firewall. That single port
            serves every server through the router - no per-server ports, and no SRV record needed.
          </p>
        </div>
      </li>
    </ol>

    <div class="flex flex-col gap-1.5">
      <label hlmLabel for="dom-name" class="text-muted-foreground text-xs uppercase tracking-wide">Domain</label>
      <input
        hlmInput
        id="dom-name"
        placeholder="gaggao.com"
        [value]="name()"
        (input)="name.set($any($event.target).value)"
        (keydown.enter)="submit()"
      />
      <span class="text-muted-foreground text-xs">Just the root domain - no <span class="font-mono">https://</span>, no <span class="font-mono">*.</span></span>
    </div>

    @if (error(); as err) {
      <p class="text-destructive text-sm">{{ err }}</p>
    }

    <div class="flex justify-end gap-2">
      <button hlmBtn variant="outline" type="button" (click)="close()" [disabled]="submitting()">Cancel</button>
      <button hlmBtn type="button" (click)="submit()" [disabled]="!canSubmit()">
        {{ submitting() ? 'Adding…' : 'Add domain' }}
      </button>
    </div>
  `,
})
export class DomainAddDialog {
  private readonly ref = inject<BrnDialogRef<unknown>>(BrnDialogRef);
  private readonly ctx = injectBrnDialogContext<DialogContext>();
  private readonly api = inject(DomainsService);

  protected readonly name = signal('');
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly canSubmit = computed(() => !this.submitting() && this.name().trim() !== '');

  protected submit(): void {
    if (!this.canSubmit()) return;
    this.submitting.set(true);
    this.error.set(null);
    this.api.apiDomainsPost({ name: this.name().trim() }).subscribe({
      next: () => {
        this.submitting.set(false);
        this.ctx.onAdded();
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
  return 'Failed to add domain';
}
