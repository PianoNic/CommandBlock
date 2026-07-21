import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { HlmTabsImports } from '@spartan-ng/helm/tabs';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';
import { ServerPropertiesForm } from './server-properties-form';
import { ServerRuntimeForm } from './server-runtime-form';
import { ServerWakeForm } from './server-wake-form';
import { ServerNetworkForm } from './server-network-form';
import { ServerIconForm } from './server-icon-form';

type DialogContext = { server: ServerInstanceDto; onSaved?: () => void };

/// One wide, tabbed "Server settings" modal that clusters everything that used to be scattered across
/// the detail header (Config + Runtime buttons) and inline controls (wake, icon) into grouped tabs.
/// The action row is pinned below the scrolling body so Save/Cancel stay reachable on every tab, and
/// Save only appears where there is something to commit - Wake and Icon write through on change, so
/// offering a Save button there would be a lie.
@Component({
  selector: 'app-server-settings-dialog',
  imports: [
    HlmButtonImports,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmTabsImports,
    ServerPropertiesForm,
    ServerRuntimeForm,
    ServerWakeForm,
    ServerNetworkForm,
    ServerIconForm,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex max-h-[85svh] flex-col gap-4 overflow-hidden' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Server settings</h3>
    </hlm-dialog-header>

    <hlm-tabs tab="general" (tabActivated)="activeTab.set($event)" class="min-h-0 flex-1">
      <hlm-tabs-list class="w-full justify-start" aria-label="Server settings sections">
        <button hlmTabsTrigger="general">General / MOTD</button>
        <button hlmTabsTrigger="runtime">Runtime</button>
        <button hlmTabsTrigger="wake">Wake &amp; sleep</button>
        <button hlmTabsTrigger="network">Network</button>
        <button hlmTabsTrigger="icon">Icon</button>
      </hlm-tabs-list>

      <div hlmTabsContent="general" class="max-h-[58svh] overflow-y-auto pr-1">
        <app-server-properties-form [server]="ctx.server" (saved)="onSaved()" />
      </div>
      <div hlmTabsContent="runtime" class="max-h-[58svh] overflow-y-auto pr-1">
        <app-server-runtime-form [server]="ctx.server" (saved)="onSaved()" />
      </div>
      <div hlmTabsContent="wake" class="max-h-[58svh] overflow-y-auto pr-1">
        <app-server-wake-form [server]="ctx.server" />
      </div>
      <div hlmTabsContent="network" class="max-h-[58svh] overflow-y-auto pr-1">
        <app-server-network-form [server]="ctx.server" (saved)="onSaved()" />
      </div>
      <div hlmTabsContent="icon" class="max-h-[58svh] overflow-y-auto pr-1">
        <app-server-icon-form [server]="ctx.server" (changed)="onSaved()" />
      </div>
    </hlm-tabs>

    <!-- Pinned: sits outside the scrolling tab body, so it never scrolls out of reach. -->
    <div class="flex items-center justify-between gap-2 border-t pt-3">
      <span class="text-muted-foreground text-xs">
        @if (appliesInstantly()) { Changes apply immediately. }
      </span>
      <div class="flex gap-2">
        <button hlmBtn size="sm" variant="outline" type="button" (click)="close()">
          {{ appliesInstantly() ? 'Close' : 'Cancel' }}
        </button>
        @if (!appliesInstantly()) {
          <button hlmBtn size="sm" type="button" (click)="save()">
            {{ needsRestart() ? 'Save & restart' : 'Save' }}
          </button>
        }
      </div>
    </div>
  `,
})
export class ServerSettingsDialog {
  protected readonly ctx = injectBrnDialogContext<DialogContext>();
  private readonly ref = inject<BrnDialogRef<unknown>>(BrnDialogRef);

  protected readonly activeTab = signal('general');

  private readonly propertiesForm = viewChild(ServerPropertiesForm);
  private readonly runtimeForm = viewChild(ServerRuntimeForm);
  private readonly networkForm = viewChild(ServerNetworkForm);

  /// Wake and Icon persist on change, so those tabs have nothing to commit.
  protected readonly appliesInstantly = computed(() => this.activeTab() === 'wake' || this.activeTab() === 'icon');

  /// Runtime env and published ports are both fixed at container-create time, so saving either one
  /// recreates the container - say so on the button rather than surprising the operator with downtime.
  protected readonly needsRestart = computed(() => this.activeTab() === 'runtime' || this.activeTab() === 'network');

  protected save(): void {
    if (this.activeTab() === 'runtime') this.runtimeForm()?.save();
    else if (this.activeTab() === 'network') this.networkForm()?.save();
    else this.propertiesForm()?.save();
  }

  protected close(): void {
    this.ref.close();
  }

  protected onSaved(): void {
    this.ctx.onSaved?.();
  }
}
