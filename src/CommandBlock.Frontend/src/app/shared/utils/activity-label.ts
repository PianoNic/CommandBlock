const LABELS: Record<string, string> = {
  'server.create': 'Created server',
  'server.create.from-backup': 'Created server from backup',
  'server.delete': 'Deleted server',
  'server.recreate': 'Applied new settings',
  'server.rename': 'Renamed server',
  'server.start': 'Started server',
  'server.stop': 'Stopped server',
  'server.restart': 'Restarted server',
  'server.sleep': 'Put server to sleep',
  'server.restore': 'Restored backup',
  'server.backup.world': 'Backed up world',
  'server.backup.server': 'Backed up server',
  'server.backup.delete': 'Deleted backup',
  'backup.schedule.add': 'Added backup schedule',
  'domain.add': 'Added domain',
  'domain.remove': 'Removed domain',
};

/// Plain-language name for an audit-log action code; unknown codes fall back to the code itself.
export function activityLabel(action: string): string {
  return LABELS[action] ?? action;
}
