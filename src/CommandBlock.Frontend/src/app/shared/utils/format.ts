/// Human file/backup size, e.g. "512 B", "3.4 MB".
export function formatBytes(value: unknown): string {
  let n = Number(value) || 0;
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let i = 0;
  while (n >= 1024 && i < units.length - 1) { n /= 1024; i++; }
  return `${n.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

/// Gigabytes with at most one decimal, dropping a trailing ".0" so caps read "2 GB" rather than "2.0 GB".
export function formatGb(bytes: number): string {
  return String(Math.round((bytes / 1024 ** 3) * 10) / 10);
}

/// Megabytes in an itzg memory value ("4G", "3072M", "512"; bare numbers are MB). 0 when unreadable.
export function memoryMb(mem: string | null | undefined): number {
  const m = /^\s*(\d+(?:\.\d+)?)\s*([gmk]?)/i.exec(mem ?? '');
  if (!m) return 0;
  const n = parseFloat(m[1]);
  switch (m[2].toLowerCase()) {
    case 'g': return Math.round(n * 1024);
    case 'k': return Math.round(n / 1024);
    default: return Math.round(n);
  }
}
