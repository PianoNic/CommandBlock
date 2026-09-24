/// One wording and colour per server state, so the dashboard, list and detail page never disagree.
export function stateLabel(state: string | null | undefined): string {
  switch (state) {
    case 'running': return 'Running';
    case 'starting': return 'Starting…';
    case 'created': return 'Starting…';
    case 'restarting': return 'Restarting…';
    case 'crashed': return 'Crashed';
    case 'sleeping': return 'Asleep - wakes on join';
    case 'exited':
    case 'stopped': return 'Stopped';
    default: return 'Unknown';
  }
}

export function stateTextClass(state: string | null | undefined): string {
  switch (state) {
    case 'running': return 'text-primary';
    case 'starting':
    case 'created':
    case 'restarting': return 'text-yellow-600 dark:text-yellow-500';
    case 'crashed': return 'text-destructive';
    case 'sleeping': return 'text-sky-500';
    default: return 'text-muted-foreground';
  }
}
