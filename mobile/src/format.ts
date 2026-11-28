import type { JobStatus, JobType } from './api/types';

/**
 * Dates and words for the screen. Times are shown in the company's zone, the same one the
 * server uses for "today" (CompanyClock), so a phone with the wrong zone set still shows the
 * window the customer was promised.
 */
export const CompanyZone = 'America/Toronto';

const time = new Intl.DateTimeFormat('en-CA', { hour: 'numeric', minute: '2-digit', timeZone: CompanyZone });
const weekday = new Intl.DateTimeFormat('en-CA', { weekday: 'long', month: 'long', day: 'numeric', timeZone: CompanyZone });
const shortDate = new Intl.DateTimeFormat('en-CA', { month: 'short', day: 'numeric', year: 'numeric', timeZone: 'UTC' });

/** "2:00 p.m." The en-CA locale writes a.m. and p.m. the Canadian way. */
export function formatTime(iso: string): string {
  return time.format(new Date(iso));
}

/** "2:00 p.m. to 4:00 p.m." */
export function formatWindow(start: string, end: string): string {
  return `${formatTime(start)} to ${formatTime(end)}`;
}

/** "Wednesday, November 25" for a `YYYY-MM-DD` business day. Noon UTC keeps it on the right date. */
export function formatDay(date: string): string {
  return weekday.format(new Date(`${date}T17:00:00Z`));
}

/** "Mar 3, 2021" for a date (DateOnly) or a moment. */
export function formatDate(iso: string): string {
  return shortDate.format(new Date(iso.length === 10 ? `${iso}T12:00:00Z` : iso));
}

/** How old the phone's copy is, in words: "just now", "12 minutes ago", "3 hours ago". */
export function formatAge(iso: string, now: Date): string {
  const minutes = Math.floor((now.getTime() - new Date(iso).getTime()) / 60_000);
  if (minutes < 1) return 'just now';
  if (minutes === 1) return 'a minute ago';
  if (minutes < 60) return `${minutes} minutes ago`;
  const hours = Math.floor(minutes / 60);
  if (hours === 1) return 'an hour ago';
  if (hours < 24) return `${hours} hours ago`;
  return `on ${formatDate(iso)}`;
}

export const statusLabel: Record<JobStatus, string> = {
  New: 'New',
  Scheduled: 'Scheduled',
  Dispatched: 'Not started',
  InProgress: 'In progress',
  Completed: 'Done',
  Cancelled: 'Cancelled',
};

export const typeLabel: Record<JobType, string> = {
  Repair: 'Repair',
  Maintenance: 'Maintenance',
  Installation: 'Installation',
  Estimate: 'Estimate',
};

/** "1 change" / "3 changes". */
export function changes(count: number): string {
  return count === 1 ? '1 change' : `${count} changes`;
}
