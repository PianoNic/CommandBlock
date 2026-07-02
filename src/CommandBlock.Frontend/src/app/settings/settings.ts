import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideTrash2, lucideGlobe } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { DomainsService } from '../api/api/domains.service';
import { DomainDto } from '../api/model/domainDto';
import { DomainAddDialog } from './domain-add-dialog';

@Component({
  selector: 'app-settings',
  imports: [ContentHeader, NgIcon, HlmButtonImports],
  providers: [provideIcons({ lucidePlus, lucideTrash2, lucideGlobe })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />

    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <div>
          <h2 class="text-sm font-medium">Domains</h2>
          <p class="text-muted-foreground text-xs">Root domains servers can be routed under.</p>
        </div>
        <button hlmBtn size="sm" type="button" (click)="addDomain()">
          <ng-icon name="lucidePlus" size="14" />
          Add domain
        </button>
      </header>

      <div class="min-h-0 flex-1 overflow-auto p-4">
        @if (loading()) {
          <p class="text-muted-foreground text-sm">Loading…</p>
        } @else if (domains().length === 0) {
          <div class="text-muted-foreground flex flex-col items-center gap-2 py-12 text-center">
            <ng-icon name="lucideGlobe" size="32" class="opacity-50" />
            <p class="text-sm">No domains yet.</p>
            <p class="text-xs">Add one to route servers under it - you'll get the DNS steps as you go.</p>
            <button hlmBtn size="sm" variant="outline" type="button" (click)="addDomain()" class="mt-1">
              <ng-icon name="lucidePlus" size="14" />
              Add domain
            </button>
          </div>
        } @else {
          <ul class="divide-border max-w-xl divide-y rounded-md border">
            @for (d of domains(); track d.id) {
              <li class="flex items-center gap-3 p-3">
                <ng-icon name="lucideGlobe" size="16" class="text-muted-foreground shrink-0" />
                <span class="flex-1 font-mono text-sm">{{ d.name }}</span>
                <span class="text-muted-foreground font-mono text-xs">*.{{ d.name }}</span>
                <button hlmBtn size="icon" variant="ghost" type="button" (click)="remove(d)" aria-label="Delete domain">
                  <ng-icon name="lucideTrash2" size="14" />
                </button>
              </li>
            }
          </ul>
        }
      </div>
    </section>
  `,
})
export class Settings {
  private readonly api = inject(DomainsService);
  private readonly dialog = inject(HlmDialogService);
  private readonly confirm = inject(ConfirmService);

  protected readonly domains = signal<ReadonlyArray<DomainDto>>([]);
  protected readonly loading = signal(false);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.api.apiDomainsGet().subscribe({
      next: (rows) => {
        this.domains.set(rows);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  protected addDomain(): void {
    this.dialog.open(DomainAddDialog, {
      context: { onAdded: () => this.load() },
      contentClass: 'sm:max-w-[520px]',
    });
  }

  protected async remove(d: DomainDto): Promise<void> {
    const ok = await this.confirm.open({
      title: `Remove ${d.name}?`,
      message: 'Existing servers keep their hostnames, but new servers can no longer pick this domain.',
      confirmLabel: 'Remove',
      destructive: true,
    });
    if (!ok) return;
    this.api.apiDomainsIdDelete(d.id).subscribe({ next: () => this.load() });
  }
}
