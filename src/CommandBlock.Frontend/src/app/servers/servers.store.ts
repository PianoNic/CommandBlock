import { computed, effect, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withHooks, withMethods, withState } from '@ngrx/signals';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';
import { ServerStatusStream } from '../shared/services/server-status.stream';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { messageOf, toastError } from '../shared/utils/errors';
import { toast } from '@spartan-ng/brain/sonner';
import { Observable } from 'rxjs';
import { memoryMb } from '../shared/utils/format';

interface ServersState {
  servers: ReadonlyArray<ServerInstanceDto>;
  loading: boolean;
  loaded: boolean;
  error: string | null;
  busy: ReadonlyArray<string>; // ids with a lifecycle action in flight
}

const initial: ServersState = { servers: [], loading: false, loaded: false, error: null, busy: [] };

/// Single source of truth for the server list + live status + lifecycle actions, shared by the
/// Servers list, the detail page and the Home dashboard. The SignalR status stream stays the live
/// transport; this store owns the merge, the membership sync and the derived stats.
export const ServersStore = signalStore(
  { providedIn: 'root' },
  withState(initial),
  withComputed((store, stream = inject(ServerStatusStream)) => ({
    // Live per-id status map from the SignalR stream (passthrough so consumers keep one source).
    statuses: computed(() => stream.statuses()),
    total: computed(() => store.servers().length),
    running: computed(() =>
      store.servers().filter((s) => (stream.statuses()[s.id]?.state ?? s.state) === 'running').length,
    ),
    playersOnline: computed(() =>
      store.servers().reduce((sum, s) => {
        const live = stream.statuses()[s.id];
        const online = live ? live.playersOnline : coerce(s.playersOnline);
        return sum + (online ?? 0);
      }, 0),
    ),
    memoryLabel: computed(() => {
      const mb = store.servers().reduce((sum, s) => sum + memoryMb(s.memory), 0);
      return mb >= 1024 ? `${(mb / 1024).toFixed(mb % 1024 === 0 ? 0 : 1)} GB` : `${mb} MB`;
    }),
    byType: computed(() => {
      const counts = new Map<string, number>();
      for (const s of store.servers()) counts.set(s.serverType, (counts.get(s.serverType) ?? 0) + 1);
      return [...counts.entries()].map(([type, count]) => ({ type, count })).sort((a, b) => b.count - a.count);
    }),
  })),
  withMethods((store, api = inject(ServerService), confirm = inject(ConfirmService), stream = inject(ServerStatusStream)) => {
    const mark = (id: string) => patchState(store, { busy: [...new Set([...store.busy(), id])] });
    const unmark = (id: string) => patchState(store, { busy: store.busy().filter((b) => b !== id) });
    const load = () => {
      patchState(store, { loading: true, error: null });
      api.apiServerGet().subscribe({
        next: (rows) => patchState(store, { servers: rows, loading: false, loaded: true }),
        error: (err: unknown) => patchState(store, { loading: false, error: messageOf(err, "Couldn't load your servers.") }),
      });
    };
    const playersOn = (s: ServerInstanceDto) => stream.statuses()[s.id]?.playersOnline ?? coerce(s.playersOnline) ?? 0;
    // Every page runs lifecycle actions through here, so confirmations and feedback read the same everywhere.
    const run = (s: ServerInstanceDto, call: Observable<unknown>, verb: string) => {
      mark(s.id);
      call.subscribe({
        next: load,
        error: (err: unknown) => { unmark(s.id); toastError(err, `Couldn't ${verb} ${s.displayName}.`); },
      });
    };
    const stopMessage = (s: ServerInstanceDto) => {
      const n = playersOn(s);
      const who = n > 0 ? `${n} player${n === 1 ? ' is' : 's are'} disconnected` : 'Players are disconnected';
      return `${who}. The world is kept and you can start it again any time.`;
    };
    return {
      load,
      isBusy: (id: string) => store.busy().includes(id),
      start(s: ServerInstanceDto) {
        run(s, api.apiServerIdStartPost(s.id), 'start');
      },
      async restart(s: ServerInstanceDto) {
        // Only worth interrupting the user for when someone is actually playing.
        if (playersOn(s) > 0) {
          const ok = await confirm.open({
            title: `Restart ${s.displayName}?`,
            message: `${stopMessage(s).split('.')[0]} for a moment while it restarts.`,
            confirmLabel: 'Restart',
          });
          if (!ok) return;
        }
        run(s, api.apiServerIdRestartPost(s.id), 'restart');
      },
      async stop(s: ServerInstanceDto) {
        const ok = await confirm.open({ title: `Stop ${s.displayName}?`, message: stopMessage(s), confirmLabel: 'Stop server', destructive: true });
        if (ok) run(s, api.apiServerIdStopPost(s.id), 'stop');
      },
      async stopAll(servers: ReadonlyArray<ServerInstanceDto>) {
        if (servers.length === 0) return;
        const players = servers.reduce((sum, s) => sum + playersOn(s), 0);
        const ok = await confirm.open({
          title: `Stop ${servers.length} running server${servers.length === 1 ? '' : 's'}?`,
          message: `${players > 0 ? `${players} player${players === 1 ? ' is' : 's are'} disconnected. ` : ''}Worlds are kept and you can start them again any time.`,
          confirmLabel: 'Stop all',
          destructive: true,
        });
        if (ok) for (const s of servers) run(s, api.apiServerIdStopPost(s.id), 'stop');
      },
      /// Resolves true once the server is gone, so a page showing it can navigate away.
      async remove(s: ServerInstanceDto): Promise<boolean> {
        const ok = await confirm.open({
          title: `Delete ${s.displayName}?`,
          message: 'This stops the server and permanently deletes its world and files. This cannot be undone.',
          confirmLabel: 'Delete server',
          destructive: true,
        });
        if (!ok) return false;
        mark(s.id);
        return new Promise((resolve) => {
          api.apiServerIdDelete(s.id).subscribe({
            next: () => { unmark(s.id); toast.success(`Deleted ${s.displayName}.`); load(); resolve(true); },
            error: (err: unknown) => { unmark(s.id); toastError(err, `Couldn't delete ${s.displayName}.`); resolve(false); },
          });
        });
      },
    };
  }),
  withHooks({
    onInit(store, stream = inject(ServerStatusStream)) {
      stream.start();
      store.load();

      // Live membership: when the stream reports a server we don't have (created elsewhere) or drops
      // one we do (deleted elsewhere), re-fetch so the list/stats stay current. State/players/memory
      // patch straight from the stream via the computeds above; this only handles rows appearing/leaving.
      effect(() => {
        if (!stream.received() || store.loading()) return;
        const live = new Set(Object.keys(stream.statuses()));
        const have = new Set(store.servers().map((s) => s.id));
        const added = [...live].some((id) => !have.has(id));
        const removed = store.servers().some((s) => !live.has(s.id));
        if (added || removed) store.load();
      });

      // Clear a busy flag once its server settles into a terminal state (or disappears).
      effect(() => {
        const st = stream.statuses();
        if (store.busy().length === 0) return;
        const settled = store.busy().filter((id) => {
          const state = st[id]?.state;
          return state === 'running' || state === 'exited' || state === 'stopped' || !(id in st);
        });
        if (settled.length > 0) patchState(store, { busy: store.busy().filter((id) => !settled.includes(id)) });
      });
    },
  }),
);

function coerce(v: unknown): number | null {
  return v == null ? null : Number(v as number);
}

