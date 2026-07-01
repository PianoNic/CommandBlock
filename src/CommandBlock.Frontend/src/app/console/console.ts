import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideTerminal } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ServerService } from '../api/api/server.service';
import { environment } from '../shared/environments/environment';

@Component({
  selector: 'app-console',
  imports: [RouterLink, NgIcon, HlmButtonImports, HlmInputImports, ContentHeader],
  providers: [provideIcons({ lucideArrowLeft, lucideTerminal })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />
    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <div class="flex items-center gap-2">
          <a hlmBtn size="sm" variant="ghost" routerLink="/servers"><ng-icon name="lucideArrowLeft" size="16" /></a>
          <h3 class="inline-flex items-center gap-2 text-sm font-medium">
            <ng-icon name="lucideTerminal" size="16" /> Console — {{ name() || 'server' }}
          </h3>
        </div>
        <span class="text-xs" [class.text-primary]="connected()" [class.text-muted-foreground]="!connected()">
          {{ connected() ? '● connected' : '○ connecting…' }}
        </span>
      </header>

      <div class="min-h-0 flex-1 overflow-hidden bg-black p-2">
        <div #term class="h-full w-full"></div>
      </div>

      <form class="flex items-center gap-2 border-t p-2" (submit)="send($event)">
        <span class="text-muted-foreground pl-2 font-mono text-sm">/</span>
        <input
          hlmInput
          class="flex-1 font-mono"
          placeholder="say hello   ·   whitelist add Steve   ·   op Steve"
          [value]="command()"
          (input)="command.set($any($event.target).value)"
          [disabled]="!connected()"
          autocomplete="off"
        />
        <button hlmBtn size="sm" type="submit" [disabled]="!connected() || command().trim() === ''">Send</button>
      </form>
    </section>
  `,
})
export class Console implements AfterViewInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(ServerService);
  private readonly oidc = inject(OidcSecurityService);
  private readonly host = viewChild.required<ElementRef<HTMLDivElement>>('term');

  private readonly serverId = this.route.snapshot.paramMap.get('id')!;
  protected readonly name = signal('');
  protected readonly connected = signal(false);
  protected readonly command = signal('');

  private term?: Terminal;
  private fit?: FitAddon;
  private connection?: HubConnection;
  private stream?: { dispose: () => void };
  private readonly onResize = () => this.fit?.fit();

  constructor() {
    this.api.apiServerGet().subscribe((rows) => {
      const s = rows.find((r) => r.id === this.serverId);
      if (s) this.name.set(s.displayName);
    });
  }

  async ngAfterViewInit(): Promise<void> {
    this.term = new Terminal({
      convertEol: true,
      fontSize: 13,
      fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
      cursorBlink: false,
      disableStdin: true,
      theme: { background: '#000000' },
    });
    this.fit = new FitAddon();
    this.term.loadAddon(this.fit);
    this.term.open(this.host().nativeElement);
    this.fit.fit();
    window.addEventListener('resize', this.onResize);

    const url = `${environment.apiBaseUrl}/hubs/console`;
    this.connection = new HubConnectionBuilder()
      .withUrl(url, { accessTokenFactory: () => firstValueFrom(this.oidc.getAccessToken()) })
      .withAutomaticReconnect()
      .build();

    this.connection.onreconnected(() => this.connected.set(true));
    this.connection.onreconnecting(() => this.connected.set(false));
    this.connection.onclose(() => this.connected.set(false));

    try {
      await this.connection.start();
      this.connected.set(true);
      this.stream = this.connection.stream('StreamLogs', this.serverId).subscribe({
        next: (chunk: string) => this.term?.write(chunk),
        error: (err: unknown) => this.term?.writeln(`\r\n[stream ended: ${String(err)}]`),
        complete: () => this.term?.writeln('\r\n[log stream closed]'),
      });
    } catch (err) {
      this.term.writeln(`\r\n[failed to connect: ${String(err)}]`);
    }
  }

  protected async send(event: Event): Promise<void> {
    event.preventDefault();
    const cmd = this.command().trim();
    if (cmd === '' || !this.connection) return;
    this.command.set('');
    this.term?.writeln(`\x1b[36m> ${cmd}\x1b[0m`);
    try {
      const output = await this.connection.invoke<string>('SendCommand', this.serverId, cmd);
      if (output?.trim()) this.term?.writeln(output.replace(/\n/g, '\r\n'));
    } catch (err) {
      this.term?.writeln(`\x1b[31m${String(err)}\x1b[0m`);
    }
  }

  ngOnDestroy(): void {
    window.removeEventListener('resize', this.onResize);
    this.stream?.dispose();
    this.connection?.stop();
    this.term?.dispose();
  }
}
