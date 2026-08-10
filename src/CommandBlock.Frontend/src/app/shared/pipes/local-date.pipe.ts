import { Pipe, PipeTransform } from '@angular/core';

export type LocalDateFormat = 'short' | 'medium' | 'time';

const OPTIONS: Record<LocalDateFormat, Intl.DateTimeFormatOptions> = {
  short: { dateStyle: 'short', timeStyle: 'short' },
  medium: { dateStyle: 'medium', timeStyle: 'short' },
  time: { timeStyle: 'short' },
};

// Constructing an Intl.DateTimeFormat is the expensive part, and these lists render a row per
// connection, so keep one formatter per style rather than one per cell.
const formatters = new Map<LocalDateFormat, Intl.DateTimeFormat>();

/// Formats a timestamp in the viewer's own locale and time zone. Angular's DatePipe resolves its
/// named formats against LOCALE_ID, which stays en-US unless every locale's data is bundled - so a
/// Swiss visitor was shown "8/10/26, 9:42 AM". Intl already knows the browser's locale, which gets
/// them "10.08.2026, 09:42" (and a 24-hour clock) without shipping locale tables.
@Pipe({ name: 'localDate' })
export class LocalDatePipe implements PipeTransform {
  transform(value: string | Date | null | undefined, format: LocalDateFormat = 'short'): string {
    if (!value) return '';
    const date = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(date.getTime())) return '';

    let formatter = formatters.get(format);
    if (!formatter) formatters.set(format, (formatter = new Intl.DateTimeFormat(undefined, OPTIONS[format])));
    return formatter.format(date);
  }
}
