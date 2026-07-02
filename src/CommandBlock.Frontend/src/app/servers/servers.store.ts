import { computed, effect, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withHooks, withMethods, withState } from '@ngrx/signals';
import { ServerService } from '../api/api/server.service';
import { ServerInstanceDto } from '../api/model/serverInstanceDto';
import { ServerStatusStream } from '../shared/services/server-status.stream';

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
      const mb = store.servers().reduce((sum, s) => sum + parseMemoryMb(s.memory), 0);
      return mb >= 1024 ? `${(mb / 1024).toFixed(mb % 1024 === 0 ? 0 : 1)} GB` : `${mb} MB`;
    }),
    byType: computed(() => {
      const counts = new Map<string, number>();
      for (const s of store.servers()) counts.set(s.serverType, (counts.get(s.serverType) ?? 0) + 1);
      return [...counts.entries()].map(([type, count]) => ({ type, count })).sort((a, b) => b.count - a.count);
    }),
  })),
  withMethods((store, api = inject(ServerService)) => {
    const mark = (id: string) => patchState(store, { busy: [...new Set([...store.busy(), id])] });
    const unmark = (id: string) => patchState(store, { busy: store.busy().filter((b) => b !== id) });
    const load = () => {
      patchState(store, { loading: true, error: null });
      api.apiServerGet().subscribe({
        next: (rows) => patchState(store, { servers: rows, loading: false, loaded: true }),
        error: () => patchState(store, { loading: false, error: 'Failed to load servers.' }),
      });
    };
    return {
      load,
      isBusy: (id: string) => store.busy().includes(id),
      start(id: string) {
        mark(id);
        api.apiServerIdStartPost(id).subscribe({ next: load, error: () => unmark(id) });
      },
      restart(id: string) {
        mark(id);
        api.apiServerIdRestartPost(id).subscribe({ next: load, error: () => unmark(id) });
      },
      stop(id: string) {
        mark(id);
        api.apiServerIdStopPost(id).subscribe({ next: load, error: () => unmark(id) });
      },
      remove(id: string) {
        api.apiServerIdDelete(id).subscribe({ next: load });
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

function parseMemoryMb(mem: string): number {
  const m = /^\s*(\d+(?:\.\d+)?)\s*([gmk]?)/i.exec(mem ?? '');
  if (!m) return 0;
  const n = parseFloat(m[1]);
  switch (m[2].toLowerCase()) {
    case 'g': return Math.round(n * 1024);
    case 'k': return Math.round(n / 1024);
    default: return Math.round(n);
  }
}
