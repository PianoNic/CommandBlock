import { ChangeDetectionStrategy, Component, OnInit, inject, input, output, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUpload, lucideTrash2 } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';
import { environment } from '../shared/environments/environment';

/// The Icon section of the server-settings modal: upload/replace/remove the server image. Uploads are
/// cropped to 64x64 server-side and also written into the container as server-icon.png.
@Component({
  selector: 'app-server-icon-form',
  imports: [HlmButtonImports, NgIcon],
  providers: [provideIcons({ lucideUpload, lucideTrash2 })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <p class="text-muted-foreground text-xs">
      Cropped to 64x64 (the Minecraft server-icon size), shown across the UI and written into the
      server as server-icon.png. Without one, the default image is used.
    </p>

    <div class="flex items-center gap-4">
      <img [src]="iconUrl()" alt="" class="h-16 w-16 border rounded-none" style="image-rendering:pixelated" />
      <div class="flex flex-col items-start gap-2">
        <button hlmBtn size="sm" variant="outline" type="button" (click)="picker.click()" [disabled]="busy()">
          <ng-icon name="lucideUpload" size="14" /> {{ hasIcon() ? 'Replace image' : 'Upload image' }}
        </button>
        @if (hasIcon()) {
          <button hlmBtn size="sm" variant="ghost" type="button" (click)="remove()" [disabled]="busy()"
            class="text-muted-foreground hover:text-destructive">
            <ng-icon name="lucideTrash2" size="14" /> Remove
          </button>
        }
      </div>
      <input #picker type="file" accept="image/png,image/jpeg,image/webp,image/gif" class="hidden" (change)="upload($event)" />
    </div>

    @if (busy()) { <span class="text-muted-foreground text-xs">working…</span> }
  `,
})
export class ServerIconForm implements OnInit {
  readonly server = input.required<ServerInstanceDto>();
  readonly changed = output<void>();

  private readonly api = inject(ServerService);
  protected readonly hasIcon = signal(false);
  protected readonly busy = signal(false);
  private readonly v = signal(0);

  ngOnInit(): void {
    this.hasIcon.set(!!this.server().hasIcon);
  }

  protected iconUrl(): string {
    const s = this.server();
    return this.hasIcon() ? `${environment.apiBaseUrl}/api/Server/${s.id}/icon?v=${this.v()}` : 'default-server-icon.png';
  }

  protected upload(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.busy.set(true);
    this.api.apiServerIdIconPost(this.server().id, file).subscribe({
      next: () => { input.value = ''; this.busy.set(false); this.hasIcon.set(true); this.v.update((x) => x + 1); this.changed.emit(); },
      error: () => { input.value = ''; this.busy.set(false); },
    });
  }

  protected remove(): void {
    this.busy.set(true);
    this.api.apiServerIdIconDelete(this.server().id).subscribe({
      next: () => { this.busy.set(false); this.hasIcon.set(false); this.v.update((x) => x + 1); this.changed.emit(); },
      error: () => { this.busy.set(false); },
    });
  }
}
