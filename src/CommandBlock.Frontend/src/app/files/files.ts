import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  OnDestroy,
  afterNextRender,
  computed,
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
import { firstValueFrom } from 'rxjs';
import { EditorView, basicSetup } from 'codemirror';
import { HighlightStyle, StreamLanguage, syntaxHighlighting } from '@codemirror/language';
import { tags } from '@lezer/highlight';
import { json } from '@codemirror/lang-json';
import { yaml } from '@codemirror/lang-yaml';
import { properties } from '@codemirror/legacy-modes/mode/properties';
import { toml } from '@codemirror/legacy-modes/mode/toml';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { FilesService } from '../api/api/files.service';
import { ServerService } from '../api/api/server.service';
import { FileEntry } from '../api/model/fileEntry';
import { formatBytes } from '../shared/utils/format';
import { messageOf, toastError } from '../shared/utils/errors';
import { toast } from '@spartan-ng/brain/sonner';
import { Title } from '@angular/platform-browser';

/// basicSetup ships a light-only look: dark-on-dark syntax colours and a white gutter once the app
/// is in dark mode. Driving the editor off the same CSS tokens as everything else means one theme
/// that follows both, and it re-resolves on a theme switch without rebuilding the editor.
const editorTheme = EditorView.theme({
  '&': { backgroundColor: 'transparent', color: 'var(--foreground)' },
  '.cm-content': { caretColor: 'var(--primary)' },
  '.cm-gutters': {
    backgroundColor: 'transparent',
    color: 'var(--muted-foreground)',
    borderRight: '1px solid var(--border)',
  },
  '.cm-activeLine': { backgroundColor: 'color-mix(in oklab, var(--muted) 45%, transparent)' },
  '.cm-activeLineGutter': { backgroundColor: 'transparent', color: 'var(--foreground)' },
  '&.cm-focused .cm-cursor': { borderLeftColor: 'var(--primary)' },
  '.cm-selectionBackground, &.cm-focused .cm-selectionBackground, .cm-content ::selection': {
    backgroundColor: 'color-mix(in oklab, var(--primary) 30%, transparent)',
  },
  '.cm-selectionMatch': { backgroundColor: 'color-mix(in oklab, var(--primary) 20%, transparent)' },
});

const editorHighlight = HighlightStyle.define([
  { tag: tags.comment, color: 'var(--code-comment)', fontStyle: 'italic' },
  // .properties and TOML come from legacy stream modes, which tag keys as definition(name) rather
  // than propertyName - cover both or those files render as one flat colour.
  { tag: [tags.propertyName, tags.definition(tags.propertyName), tags.definition(tags.name), tags.keyword, tags.tagName, tags.attributeName], color: 'var(--code-key)' },
  { tag: [tags.string, tags.special(tags.string), tags.attributeValue], color: 'var(--code-string)' },
  { tag: [tags.number, tags.bool, tags.null, tags.atom, tags.literal], color: 'var(--code-value)' },
  { tag: [tags.operator, tags.punctuation, tags.separator], color: 'var(--muted-foreground)' },
  { tag: tags.invalid, color: 'var(--destructive)' },
]);

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
  host: {
    '(document:keydown)': 'onKeydown($event)',
    '(window:beforeunload)': 'onBeforeUnload($event)',
  },
  template: `
    <app-content-header />
    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <div class="flex min-w-0 items-center gap-2">
          <a hlmBtn size="sm" variant="ghost" [routerLink]="['/servers', serverId]" aria-label="Back to server" title="Back to server"><ng-icon name="lucideArrowLeft" size="16" /></a>
          <nav aria-label="Folder path" class="truncate font-mono text-sm">
            <button class="hover:underline" (click)="goto('')">{{ name() || 'server' }}</button>
            @for (c of crumbs(); track c.path) {
              /<button class="hover:underline" (click)="goto(c.path)">{{ c.name }}</button>
            }
          </nav>
        </div>
        <div class="flex items-center gap-1.5">
          <button hlmBtn size="sm" variant="outline" (click)="newFolder()"><ng-icon name="lucideFolderPlus" size="14" /> New folder</button>
          <button hlmBtn size="sm" variant="outline" (click)="picker.click()" [disabled]="uploading()">
            <ng-icon name="lucideUpload" size="14" /> {{ uploading() ? 'Uploading…' : 'Upload' }}
          </button>
          <button hlmBtn size="sm" variant="ghost" (click)="load()" aria-label="Refresh" title="Refresh"><ng-icon name="lucideRefreshCw" size="14" [class.animate-spin]="loading()" /></button>
          <input #picker type="file" multiple class="hidden" (change)="upload($event)" />
        </div>
      </header>

      <div class="flex min-h-0 flex-1 flex-col md:flex-row">
        <!-- File list -->
        <div class="min-h-0 w-full overflow-auto border-b max-md:max-h-[40%] md:w-1/2 md:border-r md:border-b-0">
          @if (error(); as e) {
            <div class="flex items-center gap-2 p-3 text-sm">
              <p class="text-destructive">{{ e }}</p>
              <button hlmBtn size="sm" variant="outline" (click)="load()">Retry</button>
            </div>
          }
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
              <!-- Hover reveals them on desktop; keyboard focus and touch screens always see them. -->
              <span class="ml-1 flex shrink-0 items-center gap-0.5 opacity-0 group-hover:opacity-100 group-focus-within:opacity-100 max-md:opacity-100">
                @if (!en.isDirectory) {
                  <button hlmBtn size="sm" variant="ghost" class="h-6 w-6 p-0" (click)="download(en)" title="Download" [attr.aria-label]="'Download ' + en.name"><ng-icon name="lucideDownload" size="12" /></button>
                }
                <button hlmBtn size="sm" variant="ghost" class="h-6 w-6 p-0" (click)="rename(en)" title="Rename" [attr.aria-label]="'Rename ' + en.name"><ng-icon name="lucidePencil" size="12" /></button>
                <button hlmBtn size="sm" variant="ghost" class="h-6 w-6 p-0" (click)="remove(en)" title="Delete" [attr.aria-label]="'Delete ' + en.name"><ng-icon name="lucideTrash2" size="12" /></button>
              </span>
            </div>
          } @empty {
            @if (!loading() && !error()) { <p class="text-muted-foreground p-3 text-sm">Empty folder.</p> }
          }
        </div>

        <!-- Editor -->
        <div class="flex min-h-0 w-full min-w-0 flex-1 flex-col md:w-1/2">
          @if (openPath(); as op) {
            <div class="flex items-center justify-between gap-2 border-b px-3 py-1.5">
              <span class="truncate font-mono text-xs">{{ op }}@if (dirty()) { <span class="text-amber-600"> • unsaved</span> }</span>
              <button hlmBtn size="sm" (click)="save()" [disabled]="!dirty() || saving()" title="Save (Ctrl+S)">
                <ng-icon name="lucideSave" size="13" /> {{ saving() ? 'Saving…' : 'Save' }}
              </button>
            </div>
            @if (binary()) {
              <p class="text-muted-foreground p-4 text-sm">Binary file - not editable. Use download instead.</p>
            } @else {
              @if (truncated()) { <p class="text-amber-600 px-3 pt-2 text-xs">File truncated at 2 MB - saving would lose the rest; download to get the full file.</p> }
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
export class Files implements OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(FilesService);
  private readonly servers = inject(ServerService);
  private readonly confirm = inject(ConfirmService);
  private readonly injector = inject(Injector);
  private readonly title = inject(Title);

  private readonly editorHost = viewChild<ElementRef<HTMLDivElement>>('editor');

  protected readonly serverId = this.route.snapshot.paramMap.get('id')!;
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
  protected readonly uploading = signal(false);

  /// Each folder below the server root, clickable in the path breadcrumb.
  protected readonly crumbs = computed(() => {
    const parts = this.cwd().split('/').filter(Boolean);
    return parts.map((name, i) => ({ name, path: parts.slice(0, i + 1).join('/') }));
  });

  private editor?: EditorView;

  constructor() {
    // Server name for the header (best-effort).
    this.servers.apiServerGet().subscribe((rows) => {
      const s = rows.find((r) => r.id === this.serverId);
      if (s) { this.name.set(s.displayName); this.title.setTitle(`Files - ${s.displayName} · CommandBlock`); }
    });
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.apiServerServerIdFilesGet(this.serverId, this.cwd()).subscribe({
      next: (rows) => { this.entries.set(rows); this.loading.set(false); },
      error: (e) => { this.error.set(messageOf(e, "Couldn't load this folder.")); this.loading.set(false); },
    });
  }

  protected goto(path: string): void { this.cwd.set(path); this.load(); }
  protected up(): void { const c = this.cwd(); this.goto(c.includes('/') ? c.slice(0, c.lastIndexOf('/')) : ''); }

  protected clickEntry(en: FileEntry): void {
    const path = this.join(en.name);
    if (en.isDirectory) { this.cwd.set(path); this.load(); return; }
    this.openFile(path);
  }

  private async openFile(path: string): Promise<void> {
    if (path === this.openPath() || !(await this.confirmDiscard())) return;
    this.api.apiServerServerIdFilesContentGet(this.serverId, path).subscribe({
      next: (c) => {
        this.error.set(null);
        this.openPath.set(path);
        this.binary.set(c.binary);
        this.truncated.set(c.truncated);
        this.dirty.set(false);
        // Mount only after Angular renders the @if branch holding the #editor host - otherwise the
        // viewChild is still null and the editor silently never appears (the old queueMicrotask race).
        if (!c.binary) afterNextRender(() => this.mountEditor(c.content, path), { injector: this.injector });
      },
      error: (e) => toastError(e, "Couldn't open that file."),
    });
  }

  /// True when there's nothing unsaved, or the user agreed to throw it away.
  private async confirmDiscard(): Promise<boolean> {
    if (!this.dirty()) return true;
    return this.confirm.open({
      title: 'Discard unsaved changes?',
      message: `Your edits to ${this.openPath()} haven't been saved.`,
      confirmLabel: 'Discard changes',
      destructive: true,
    });
  }

  /// Route guard hook: leaving the page with unsaved edits asks first.
  canLeave(): Promise<boolean> { return this.confirmDiscard(); }

  protected onBeforeUnload(e: BeforeUnloadEvent): void {
    if (this.dirty()) e.preventDefault();
  }

  protected onKeydown(e: KeyboardEvent): void {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's' && this.openPath()) {
      e.preventDefault();
      if (this.dirty() && !this.saving()) this.save();
    }
  }

  private mountEditor(content: string, path: string): void {
    const host = this.editorHost()?.nativeElement;
    if (!host) return;
    this.editor?.destroy();
    host.replaceChildren();
    this.editor = new EditorView({
      doc: content,
      extensions: [
        basicSetup,
        editorTheme,
        // defaultHighlightStyle inside basicSetup registers itself as a fallback, so this wins.
        syntaxHighlighting(editorHighlight),
        this.languageFor(path),
        EditorView.updateListener.of((u) => { if (u.docChanged) this.dirty.set(true); }),
      ],
      parent: host,
    });
  }

  /// Picks a CodeMirror language by extension for the common Minecraft config formats.
  private languageFor(path: string) {
    switch (path.split('.').pop()?.toLowerCase()) {
      case 'json':
      case 'mcmeta':
        return json();
      case 'yml':
      case 'yaml':
        return yaml();
      case 'properties':
        return StreamLanguage.define(properties);
      case 'toml':
        return StreamLanguage.define(toml);
      default:
        return [];
    }
  }

  protected save(): void {
    const path = this.openPath();
    if (!path || !this.editor || this.saving()) return;
    this.saving.set(true);
    this.api.apiServerServerIdFilesContentPut(this.serverId, { path, content: this.editor.state.doc.toString() }).subscribe({
      next: () => { this.saving.set(false); this.dirty.set(false); toast.success(`Saved ${path.split('/').pop()}.`); },
      error: (e) => { this.saving.set(false); toastError(e, "Couldn't save the file."); },
    });
  }

  protected async newFolder(): Promise<void> {
    const name = await this.confirm.prompt({
      title: 'New folder',
      label: 'Folder name',
      placeholder: 'e.g. plugins',
      confirmLabel: 'Create folder',
      validate: (v) => this.nameProblem(v),
    });
    if (!name) return;
    this.api.apiServerServerIdFilesMkdirPost(this.serverId, { path: this.join(name) }).subscribe({
      next: () => this.load(),
      error: (e) => toastError(e, "Couldn't create the folder."),
    });
  }

  protected upload(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (files.length === 0) return;
    const existing = new Set(this.entries().map((e) => e.name));
    void this.uploadAll(files, files.filter((f) => existing.has(f.name)).map((f) => f.name));
  }

  private async uploadAll(files: File[], clashes: string[]): Promise<void> {
    if (clashes.length > 0) {
      const ok = await this.confirm.open({
        title: clashes.length === 1 ? `Replace ${clashes[0]}?` : `Replace ${clashes.length} files?`,
        message: `${clashes.length === 1 ? 'A file with that name already exists' : 'These already exist: ' + clashes.join(', ')}. Uploading overwrites ${clashes.length === 1 ? 'it' : 'them'}.`,
        confirmLabel: 'Replace',
        destructive: true,
      });
      if (!ok) return;
    }
    this.uploading.set(true);
    let done = 0;
    for (const file of files) {
      try {
        await firstValueFrom(this.api.apiServerServerIdFilesUploadPost(this.serverId, this.cwd(), file));
        done++;
      } catch (e) {
        toastError(e, `Couldn't upload ${file.name}.`);
      }
    }
    this.uploading.set(false);
    if (done > 0) toast.success(done === 1 ? `Uploaded ${files.length === 1 ? files[0].name : '1 file'}.` : `Uploaded ${done} files.`);
    this.load();
  }

  protected async rename(en: FileEntry): Promise<void> {
    const next = await this.confirm.prompt({
      title: `Rename ${en.name}`,
      label: 'New name',
      value: en.name,
      confirmLabel: 'Rename',
      validate: (v) => (v === en.name ? null : this.nameProblem(v)),
    });
    if (!next || next === en.name) return;
    this.api.apiServerServerIdFilesRenamePost(this.serverId, { from: this.join(en.name), to: this.join(next) }).subscribe({
      next: () => {
        if (this.openPath() === this.join(en.name)) this.openPath.set(this.join(next));
        this.load();
      },
      error: (e) => toastError(e, `Couldn't rename ${en.name}.`),
    });
  }

  private nameProblem(name: string): string | null {
    if (name.includes('/') || name.includes('\\')) return "Names can't contain slashes.";
    if (name === '.' || name === '..') return 'Pick a different name.';
    if (this.entries().some((e) => e.name === name)) return 'Something with that name already exists here.';
    return null;
  }

  protected async remove(en: FileEntry): Promise<void> {
    const ok = await this.confirm.open({
      title: `Delete ${en.name}?`,
      message: en.isDirectory ? 'The folder and everything in it will be deleted.' : 'This file will be deleted.',
      confirmLabel: 'Delete', destructive: true,
    });
    if (!ok) return;
    this.api.apiServerServerIdFilesDelete(this.serverId, this.join(en.name)).subscribe({
      next: () => {
        if (this.openPath() === this.join(en.name)) { this.openPath.set(null); this.dirty.set(false); }
        this.load();
      },
      error: (e) => toastError(e, `Couldn't delete ${en.name}.`),
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
      error: (e) => toastError(e, `Couldn't download ${en.name}.`),
    });
  }

  protected size(en: FileEntry): string { return formatBytes(en.size); }

  private join(name: string): string { const c = this.cwd(); return c ? `${c}/${name}` : name; }

  ngOnDestroy(): void { this.editor?.destroy(); }
}
