import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucideFolder,
  lucideFile,
  lucideUpload,
  lucideFolderPlus,
  lucideTrash2,
  lucidePencil,
  lucideDownload,
  lucideSave,
  lucideRefreshCw,
} from '@ng-icons/lucide';
import { EditorView, basicSetup } from 'codemirror';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { FilesService } from '../api/api/files.service';
import { ServerService } from '../api/api/server.service';
import { FileEntry } from '../api/model/fileEntry';

@Component({
  selector: 'app-files',
  imports: [RouterLink, NgIcon, HlmButtonImports, ContentHeader],
  providers: [
    provideIcons({
      lucideArrowLeft, lucideFolder, lucideFile, lucideUpload, lucideFolderPlus,
      lucideTrash2, lucidePencil, lucideDownload, lucideSave, lucideRefreshCw,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />
    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <div class="flex min-w-0 items-center gap-2">
          <a hlmBtn size="sm" variant="ghost" routerLink="/servers"><ng-icon name="lucideArrowLeft" size="16" /></a>
          <span class="truncate font-mono text-sm">
            <button class="hover:underline" (click)="goto('')">{{ name() || 'server' }}</button>{{ cwd() ? '/' + cwd() : '' }}
          </span>
        </div>
        <div class="flex items-center gap-1.5">
          <button hlmBtn size="sm" variant="outline" (click)="newFolder()"><ng-icon name="lucideFolderPlus" size="14" /> New folder</button>
          <button hlmBtn size="sm" variant="outline" (click)="picker.click()"><ng-icon name="lucideUpload" size="14" /> Upload</button>
          <button hlmBtn size="sm" variant="ghost" (click)="load()"><ng-icon name="lucideRefreshCw" size="14" [class.animate-spin]="loading()" /></button>
          <input #picker type="file" class="hidden" (change)="upload($event)" />
        </div>
      </header>

      <div class="flex min-h-0 flex-1">
        <!-- File list -->
        <div class="w-1/2 min-h-0 overflow-auto border-r">
          @if (error(); as e) { <p class="text-destructive p-3 text-sm">{{ e }}</p> }
          @if (cwd()) {
            <button class="hover:bg-accent flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm" (click)="up()">
              <ng-icon name="lucideFolder" size="15" class="opacity-60" /> ..
            </button>
          }
          @for (en of entries(); track en.name) {
            <div class="hover:bg-accent group flex items-center gap-2 px-3 py-1.5 text-sm">
              <button class="flex min-w-0 flex-1 items-center gap-2 text-left" (click)="clickEntry(en)">
                <ng-icon [name]="en.isDirectory ? 'lucideFolder' : 'lucideFile'" size="15" [class.opacity-60]="en.isDirectory" class="shrink-0" />
                <span class="truncate">{{ en.name }}</span>
                @if (!en.isDirectory) { <span class="text-muted-foreground ml-auto shrink-0 text-xs">{{ size(en) }}</span> }
              </button>
              <span class="ml-1 flex shrink-0 items-center gap-0.5 opacity-0 group-hover:opacity-100">
                @if (!en.isDirectory) {
                  <button hlmBtn size="sm" variant="ghost" class="h-6 w-6 p-0" (click)="download(en)" title="Download"><ng-icon name="lucideDownload" size="12" /></button>
                }
                <button hlmBtn size="sm" variant="ghost" class="h-6 w-6 p-0" (click)="rename(en)" title="Rename"><ng-icon name="lucidePencil" size="12" /></button>
                <button hlmBtn size="sm" variant="ghost" class="h-6 w-6 p-0" (click)="remove(en)" title="Delete"><ng-icon name="lucideTrash2" size="12" /></button>
              </span>
            </div>
          } @empty {
            @if (!loading()) { <p class="text-muted-foreground p-3 text-sm">Empty folder.</p> }
          }
        </div>

        <!-- Editor -->
        <div class="flex w-1/2 min-w-0 flex-col">
          @if (openPath(); as op) {
            <div class="flex items-center justify-between gap-2 border-b px-3 py-1.5">
              <span class="truncate font-mono text-xs">{{ op }}{{ dirty() ? ' •' : '' }}</span>
              <button hlmBtn size="sm" (click)="save()" [disabled]="!dirty() || saving()">
                <ng-icon name="lucideSave" size="13" /> {{ saving() ? 'Saving…' : 'Save' }}
              </button>
            </div>
            @if (binary()) {
              <p class="text-muted-foreground p-4 text-sm">Binary file — not editable. Use download instead.</p>
            } @else {
              @if (truncated()) { <p class="text-amber-600 px-3 pt-2 text-xs">File truncated at 2 MB — saving would lose the rest; download to get the full file.</p> }
              <div #editor class="min-h-0 flex-1 overflow-auto text-sm"></div>
            }
          } @else {
            <p class="text-muted-foreground p-4 text-sm">Select a file to view or edit.</p>
          }
        </div>
      </div>
    </section>
  `,
})
export class Files implements AfterViewInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(FilesService);
  private readonly servers = inject(ServerService);
  private readonly confirm = inject(ConfirmService);

  private readonly editorHost = viewChild<ElementRef<HTMLDivElement>>('editor');

  private readonly serverId = this.route.snapshot.paramMap.get('id')!;
  protected readonly name = signal('');
  protected readonly cwd = signal('');
  protected readonly entries = signal<ReadonlyArray<FileEntry>>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly openPath = signal<string | null>(null);
  protected readonly binary = signal(false);
  protected readonly truncated = signal(false);
  protected readonly dirty = signal(false);
  protected readonly saving = signal(false);

  private editor?: EditorView;

  constructor() {
    // Server name for the header (best-effort).
    this.servers.apiServerGet().subscribe((rows) => {
      const s = rows.find((r) => r.id === this.serverId);
      if (s) this.name.set(s.displayName);
    });
    this.load();
  }

  ngAfterViewInit(): void { /* editor is created lazily when a file opens */ }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.apiServerServerIdFilesGet(this.serverId, this.cwd()).subscribe({
      next: (rows) => { this.entries.set(rows); this.loading.set(false); },
      error: (e) => { this.error.set(messageOf(e)); this.loading.set(false); },
    });
  }

  protected goto(path: string): void { this.cwd.set(path); this.load(); }
  protected up(): void { const c = this.cwd(); this.goto(c.includes('/') ? c.slice(0, c.lastIndexOf('/')) : ''); }

  protected clickEntry(en: FileEntry): void {
    const path = this.join(en.name);
    if (en.isDirectory) { this.cwd.set(path); this.load(); return; }
    this.openFile(path);
  }

  private openFile(path: string): void {
    this.api.apiServerServerIdFilesContentGet(this.serverId, path).subscribe({
      next: (c) => {
        this.openPath.set(path);
        this.binary.set(c.binary);
        this.truncated.set(c.truncated);
        this.dirty.set(false);
        if (!c.binary) queueMicrotask(() => this.mountEditor(c.content));
      },
      error: (e) => this.error.set(messageOf(e)),
    });
  }

  private mountEditor(content: string): void {
    const host = this.editorHost()?.nativeElement;
    if (!host) return;
    this.editor?.destroy();
    host.replaceChildren();
    this.editor = new EditorView({
      doc: content,
      extensions: [basicSetup, EditorView.updateListener.of((u) => { if (u.docChanged) this.dirty.set(true); })],
      parent: host,
    });
  }

  protected save(): void {
    const path = this.openPath();
    if (!path || !this.editor) return;
    this.saving.set(true);
    this.api.apiServerServerIdFilesContentPut(this.serverId, { path, content: this.editor.state.doc.toString() }).subscribe({
      next: () => { this.saving.set(false); this.dirty.set(false); },
      error: (e) => { this.saving.set(false); this.error.set(messageOf(e)); },
    });
  }

  protected newFolder(): void {
    const nameInput = window.prompt('New folder name:');
    if (!nameInput?.trim()) return;
    this.api.apiServerServerIdFilesMkdirPost(this.serverId, { path: this.join(nameInput.trim()) }).subscribe({
      next: () => this.load(), error: (e) => this.error.set(messageOf(e)),
    });
  }

  protected upload(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.api.apiServerServerIdFilesUploadPost(this.serverId, this.cwd(), file).subscribe({
      next: () => { input.value = ''; this.load(); },
      error: (e) => { input.value = ''; this.error.set(messageOf(e)); },
    });
  }

  protected async rename(en: FileEntry): Promise<void> {
    const next = window.prompt('Rename to:', en.name);
    if (!next?.trim() || next === en.name) return;
    const parent = this.cwd();
    const to = parent ? `${parent}/${next.trim()}` : next.trim();
    this.api.apiServerServerIdFilesRenamePost(this.serverId, { from: this.join(en.name), to }).subscribe({
      next: () => this.load(), error: (e) => this.error.set(messageOf(e)),
    });
  }

  protected async remove(en: FileEntry): Promise<void> {
    const ok = await this.confirm.open({
      title: `Delete ${en.name}?`,
      message: en.isDirectory ? 'The folder and everything in it will be deleted.' : 'This file will be deleted.',
      confirmLabel: 'Delete', destructive: true,
    });
    if (!ok) return;
    this.api.apiServerServerIdFilesDelete(this.serverId, this.join(en.name)).subscribe({
      next: () => { if (this.openPath() === this.join(en.name)) this.openPath.set(null); this.load(); },
      error: (e) => this.error.set(messageOf(e)),
    });
  }

  protected download(en: FileEntry): void {
    // Blob download through the generated client (auth header is applied by the interceptor).
    this.api.apiServerServerIdFilesDownloadGet(this.serverId, this.join(en.name), 'body', false, {
      httpHeaderAccept: 'application/octet-stream' as never,
    }).subscribe({
      next: (data: unknown) => {
        const blob = data instanceof Blob ? data : new Blob([data as BlobPart]);
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url; a.download = en.name; a.click();
        URL.revokeObjectURL(url);
      },
      error: (e) => this.error.set(messageOf(e)),
    });
  }

  protected size(en: FileEntry): string {
    let n = Number(en.size as unknown as number) || 0;
    const u = ['B', 'KB', 'MB', 'GB']; let i = 0;
    while (n >= 1024 && i < u.length - 1) { n /= 1024; i++; }
    return `${n.toFixed(i === 0 ? 0 : 1)} ${u[i]}`;
  }

  private join(name: string): string { const c = this.cwd(); return c ? `${c}/${name}` : name; }

  ngOnDestroy(): void { this.editor?.destroy(); }
}

function messageOf(err: unknown): string {
  if (err && typeof err === 'object' && 'error' in err) {
    const e = (err as { error: unknown }).error;
    if (e && typeof e === 'object' && 'error' in e) return String((e as { error: unknown }).error);
  }
  return err instanceof Error ? err.message : 'Request failed';
}
