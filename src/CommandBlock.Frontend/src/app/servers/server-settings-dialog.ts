import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';
import { ServerPropertiesForm } from './server-properties-form';
import { ServerRuntimeForm } from './server-runtime-form';
import { ServerWakeForm } from './server-wake-form';
import { ServerIconForm } from './server-icon-form';

type SettingsTab = 'general' | 'runtime' | 'wake' | 'icon';
type DialogContext = { server: ServerInstanceDto; onSaved?: () => void };

/// One wide, tabbed "Server settings" modal that clusters everything that used to be scattered across
/// the detail header (Config + Runtime buttons) and inline controls (wake, icon) into grouped tabs.
@Component({
  selector: 'app-server-settings-dialog',
  imports: [
    HlmButtonImports,
    HlmDialogHeader,
    HlmDialogTitle,
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

    <div class="flex flex-wrap gap-1 border-b pb-2">
      @for (t of tabs; track t.id) {
        <button hlmBtn size="sm" [variant]="active() === t.id ? 'secondary' : 'ghost'" type="button" (click)="active.set(t.id)">
          {{ t.label }}
        </button>
      }
    </div>

    <div class="min-h-0 flex-1 overflow-y-auto pr-1">
      @switch (active()) {
        @case ('general') { <app-server-properties-form [server]="ctx.server" (saved)="onSaved()" /> }
        @case ('runtime') { <app-server-runtime-form [server]="ctx.server" (saved)="onSaved()" /> }
        @case ('wake') { <app-server-wake-form [server]="ctx.server" /> }
        @case ('icon') { <app-server-icon-form [server]="ctx.server" (changed)="onSaved()" /> }
      }
    </div>
  `,
})
export class ServerSettingsDialog {
  protected readonly ctx = injectBrnDialogContext<DialogContext>();
  protected readonly active = signal<SettingsTab>('general');
  protected readonly tabs: ReadonlyArray<{ id: SettingsTab; label: string }> = [
    { id: 'general', label: 'General / MOTD' },
    { id: 'runtime', label: 'Runtime' },
    { id: 'wake', label: 'Wake on join' },
    { id: 'icon', label: 'Icon' },
  ];

  protected onSaved(): void {
    this.ctx.onSaved?.();
  }
}
