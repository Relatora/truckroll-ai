import { describe, expect, it } from 'vitest';
import { changes, formatAge, formatDay, formatWindow } from './format';

describe('format', () => {
  it("shows a job's window in the company's time zone, whatever the phone's", () => {
    // 19:00 UTC is 2 p.m. in Ontario in November (EST, UTC-5).
    expect(formatWindow('2026-11-25T19:00:00Z', '2026-11-25T21:00:00Z')).toBe('2:00 p.m. to 4:00 p.m.');
  });

  it('names the business day without slipping a day at either end', () => {
    expect(formatDay('2026-11-25')).toBe('Wednesday, November 25');
  });

  it('says how old the copy on the phone is in words', () => {
    const now = new Date('2026-11-25T15:00:00Z');

    expect(['2026-11-25T14:59:30Z', '2026-11-25T14:48:00Z', '2026-11-25T12:00:00Z'].map((at) => formatAge(at, now))).toEqual([
      'just now',
      '12 minutes ago',
      '3 hours ago',
    ]);
  });

  it('counts changes in plain words', () => {
    expect([changes(1), changes(3)]).toEqual(['1 change', '3 changes']);
  });
});
