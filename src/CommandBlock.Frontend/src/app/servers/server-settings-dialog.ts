import { ChangeDetectionStrategy, Component } from '@angular/core';
import { injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { HlmTabsImports } from '@spartan-ng/helm/tabs';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';
import { ServerPropertiesForm } from './server-properties-form';
import { ServerRuntimeForm } from './server-runtime-form';
import { ServerWakeForm } from './server-wake-form';
import { ServerIconForm } from './server-icon-form';

type DialogContext = { server: ServerInstanceDto; onSaved?: () => void };

/// One wide, tabbed "Server settings" modal that clusters everything that used to be scattered across
/// the detail header (Config + Runtime buttons) and inline controls (wake, icon) into grouped tabs.
@Component({
  selector: 'app-server-settings-dialog',
  imports: [
    HlmDialogHeader,
    HlmDialogTitle,
    HlmTabsImports,
    ServerPropertiesForm,
    ServerRuntimeForm,
    ServerWakeForm,
    ServerIconForm,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex max-h-[85svh] flex-col gap-4 overflow-hidden' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Server settings - {{ ctx.server.displayName }}</h3>
    </hlm-dialog-header>

    <hlm-tabs tab="general" class="min-h-0 flex-1">
      <hlm-tabs-list class="w-full justify-start" aria-label="Server settings sections">
        <button hlmTabsTrigger="general">General / MOTD</button>
        <button hlmTabsTrigger="runtime">Runtime</button>
        <button hlmTabsTrigger="wake">Wake &amp; sleep</button>
        <button hlmTabsTrigger="icon">Icon</button>
      </hlm-tabs-list>

      <div hlmTabsContent="general" class="max-h-[65svh] overflow-y-auto pr-1">
        <app-server-properties-form [server]="ctx.server" (saved)="onSaved()" />
      </div>
      <div hlmTabsContent="runtime" class="max-h-[65svh] overflow-y-auto pr-1">
        <app-server-runtime-form [server]="ctx.server" (saved)="onSaved()" />
      </div>
      <div hlmTabsContent="wake" class="max-h-[65svh] overflow-y-auto pr-1">
        <app-server-wake-form [server]="ctx.server" />
      </div>
      <div hlmTabsContent="icon" class="max-h-[65svh] overflow-y-auto pr-1">
        <app-server-icon-form [server]="ctx.server" (changed)="onSaved()" />
      </div>
    </hlm-tabs>
  `,
})
export class ServerSettingsDialog {
  protected readonly ctx = injectBrnDialogContext<DialogContext>();

  protected onSaved(): void {
    this.ctx.onSaved?.();
  }
}
