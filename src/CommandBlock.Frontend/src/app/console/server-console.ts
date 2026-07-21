import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTerminal } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { environment } from '../shared/environments/environment';

/// Embeddable live console for one server: xterm log stream + RCON command input over the
/// /hubs/console SignalR hub. Self-contained (connects on init, cleans up on destroy) so it can be
/// dropped into the detail page or a full-screen route alike.
@Component({
  selector: 'app-server-console',
  imports: [NgIcon, HlmButtonImports, HlmInputImports],
  providers: [provideIcons({ lucideTerminal })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <div class="min-h-0 flex-1 overflow-hidden bg-black p-2">
      <div #term class="h-full w-full"></div>
    </div>

    <form class="flex items-center gap-2 border-t p-2" (submit)="send($event)">
      <input
        hlmInput
        class="flex-1 font-mono"
        placeholder="Enter a command"
        [value]="command()"
        (input)="command.set($any($event.target).value)"
        [disabled]="!connected()"
        autocomplete="off"
      />
      <button hlmBtn size="sm" type="submit" [disabled]="!connected() || command().trim() === ''">Send</button>
    </form>
  `,
})
export class ServerConsole implements AfterViewInit, OnDestroy {
  private readonly oidc = inject(OidcSecurityService);
  private readonly host = viewChild.required<ElementRef<HTMLDivElement>>('term');

  readonly serverId = input.required<string>();

  protected readonly connected = signal(false);
  protected readonly command = signal('');

  private term?: Terminal;
  private fit?: FitAddon;
  private connection?: HubConnection;
  private stream?: { dispose: () => void };
  private lineBuf = '';
  private readonly onResize = () => this.fit?.fit();

  /// Buffers the raw stream into whole lines, then writes each one colourised (MC logs carry no ANSI
  /// of their own since there's no TTY).
  private handleChunk(chunk: string): void {
    this.lineBuf += chunk;
    const lines = this.lineBuf.split('\n');
    this.lineBuf = lines.pop() ?? '';
    for (const line of lines) {
      const clean = line.replace(/\r$/, '');
      if (isOwnRconNoise(clean)) continue;
      this.term?.writeln(colorizeLogLine(clean));
    }
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
    // xterm swallows Ctrl+C as SIGINT, so selecting log output and copying silently did nothing.
    // Returning false hands the event back to the browser, which then does a normal copy. Only when
    // there's actually a selection, so the shortcut still behaves normally otherwise.
    this.term.attachCustomKeyEventHandler((event) => {
      const copyCombo = (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'c';
      if (event.type === 'keydown' && copyCombo && this.term?.hasSelection()) return false;
      return true;
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
      this.stream = this.connection.stream('StreamLogs', this.serverId()).subscribe({
        next: (chunk: string) => this.handleChunk(chunk),
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
    // The command itself isn't echoed - the server already logs what it did, so echoing it just
    // duplicates the line above the response.
    try {
      const output = await this.connection.invoke<string>('SendCommand', this.serverId(), cmd);
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

const RESET = '\x1b[0m';
// Minecraft § colour/format codes -> ANSI SGR (so coloured in-game messages show in the console).
const SECTION_ANSI: Record<string, string> = {
  '0': '\x1b[30m', '1': '\x1b[34m', '2': '\x1b[32m', '3': '\x1b[36m', '4': '\x1b[31m', '5': '\x1b[35m',
  '6': '\x1b[33m', '7': '\x1b[37m', '8': '\x1b[90m', '9': '\x1b[94m', 'a': '\x1b[92m', 'b': '\x1b[96m',
  'c': '\x1b[91m', 'd': '\x1b[95m', 'e': '\x1b[93m', 'f': '\x1b[97m',
  'l': '\x1b[1m', 'o': '\x1b[3m', 'n': '\x1b[4m', 'm': '\x1b[9m', 'r': RESET, 'k': '',
};

const GRAY = '\x1b[90m', GREEN = '\x1b[92m', YELLOW = '\x1b[33m', RED = '\x1b[91m';

/// Colourises one Minecraft server log line for xterm. Handles both `[HH:MM:SS] [Thread/LEVEL]:`
/// (vanilla) and `[HH:MM:SS LEVEL]:` (Paper): dims the bracketed prefix, colours the LEVEL word
/// (INFO green, WARN yellow, ERROR red), tints the message for warnings/errors, and converts any
/// § colour codes to ANSI. Always resets so colour never bleeds into the next line.
function colorizeLogLine(raw: string): string {
  if (raw === '') return '';
  const isErr = /\b(?:ERROR|SEVERE|FATAL)\b/.test(raw);
  const isWarn = /\bWARN(?:ING)?\b/.test(raw);
  const lvlColor = isErr ? RED : isWarn ? YELLOW : GREEN;
  const msgTint = isErr ? RED : isWarn ? YELLOW : '';

  let s = raw.replace(/§([0-9a-fk-orA-FK-OR])/g, (_m, c: string) => SECTION_ANSI[c.toLowerCase()] ?? '');
  // Dim the leading bracket prefix(es) but colour the level word inside them.
  s = s.replace(/^(?:\[[^\]]*\]\s*)+/, (prefix) =>
    GRAY + prefix.replace(/\b(INFO|WARN(?:ING)?|ERROR|SEVERE|FATAL|DEBUG)\b/g, `${lvlColor}$1${GRAY}`) + RESET + msgTint,
  );
  return msgTint + s + RESET;
}

/// CommandBlock opens a short-lived RCON connection for player counts and console commands, and the
/// server logs a thread start plus shutdown for each one. That's our own plumbing talking about
/// itself - it drowns real output and tells the operator nothing, so it never reaches the terminal.
function isOwnRconNoise(line: string): boolean {
  return /Thread RCON Client .*(started|shutting down)/.test(line)
    || /\[RCON Listener [^\]]*\]: Thread RCON Client/.test(line)
    // Older servers word it differently: "Rcon connection from: /172.20.0.5".
    || /Rcon connection from:/i.test(line)
    || isAnonymousProbe(line);
}

/// Status pings leave a trace on old servers: anything pre-1.4 logs a line for every socket that
/// closes, so CommandBlock's own health checks scroll past as connection noise. The server itself
/// draws the line we need - a real player is always named ("PianoNic [/1.2.3.4:5] lost connection"),
/// while a bare address is something that never logged in. Only the nameless ones are dropped, so a
/// player joining or leaving is never hidden.
function isAnonymousProbe(line: string): boolean {
  // A named client's address is bracketed ("PianoNic [/1.2.3.4:5] lost connection"), so requiring the
  // address to sit bare against the message is what separates a probe from a player.
  return /(?:^|\s)\/[0-9a-fA-F.:]+:\d+ lost connection\s*$/.test(line)
    || /(?:^|\s)Disconnecting \/[0-9a-fA-F.:]+:\d+:/.test(line);
}
