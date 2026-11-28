import { describe, expect, it } from 'vitest';
import {
  applyDay,
  applySyncResponse,
  canComplete,
  canStart,
  emptyState,
  enqueue,
  makeChange,
  nextBatch,
  projectDay,
  type QueuedChange,
} from './queue';
import { day, job } from './testing';

const at = new Date('2026-11-25T14:30:00Z');

function change(kind: QueuedChange['kind'], id: string, input = {}): QueuedChange {
  const made = makeChange(kind, job(), { body: 'Note.', resolution: 'Replaced the ignitor.', ...input }, id, at);
  if ('error' in made) throw new Error(made.error);
  return made.change;
}

describe('makeChange', () => {
  it('refuses an empty note and a completion without a resolution, as the server would', () => {
    expect(makeChange('note', job(), { body: '   ' }, 'c1', at)).toEqual({ error: 'Write the note first.' });
    expect(makeChange('complete', job(), {}, 'c1', at)).toEqual({ error: 'Say what you found and what you did.' });
  });

  it('trims the text, stamps the time and treats a completion as a first-time fix unless told otherwise', () => {
    const made = makeChange('complete', job(), { resolution: '  Replaced the ignitor. ' }, 'c1', at);

    expect(made).toEqual({
      change: {
        id: 'c1',
        jobId: 1,
        jobNumber: 'J-24051',
        kind: 'complete',
        madeAt: '2026-11-25T14:30:00.000Z',
        body: null,
        resolution: 'Replaced the ignitor.',
        firstTimeFix: true,
      },
    });
  });

  it('sends only the fields that belong to the kind', () => {
    const made = makeChange('start', job(), { body: 'stray', resolution: 'stray', firstTimeFix: false }, 'c1', at);

    expect(made).toMatchObject({ change: { body: null, resolution: null, firstTimeFix: null } });
  });
});

describe('projectDay', () => {
  it('shows a start and a completion before the server has them', () => {
    const view = projectDay(day(), [change('start', 'c1'), change('complete', 'c2')]);

    expect(view.jobs[0]).toMatchObject({ status: 'Completed', resolution: 'Replaced the ignitor.', waiting: 2 });
  });

  it("shows a waiting note in the technician's name, marked as waiting", () => {
    const view = projectDay(day(), [change('note', 'c1', { body: 'Dog in the yard.' })]);

    expect(view.jobs[0].notes).toEqual([
      { author: 'Nadia Kowalczyk', source: 'Technician', body: 'Dog in the yard.', createdAt: at.toISOString(), waiting: true },
    ]);
  });

  it("never edits the server's copy", () => {
    const serverDay = day();
    projectDay(serverDay, [change('start', 'c1'), change('note', 'c2')]);

    expect(serverDay.jobs[0].status).toBe('Dispatched');
    expect(serverDay.jobs[0].notes).toEqual([]);
  });

  it("leaves a job alone if the server's copy says it's already past that step", () => {
    const view = projectDay(day([job({ status: 'Completed', resolution: 'Done by the office.' })]), [change('complete', 'c1')]);

    expect(view.jobs[0]).toMatchObject({ status: 'Completed', resolution: 'Done by the office.', waiting: 1 });
  });

  it("only touches the job a change is for", () => {
    const view = projectDay(day([job(), job({ id: 2, number: 'J-24052' })]), [change('start', 'c1')]);

    expect(view.jobs.map((j) => [j.status, j.waiting])).toEqual([
      ['InProgress', 1],
      ['Dispatched', 0],
    ]);
  });
});

describe('which actions a job offers', () => {
  it('starts only a dispatched job, and completes it straight from dispatched too', () => {
    expect([canStart(job({ status: 'Dispatched' })), canStart(job({ status: 'InProgress' }))]).toEqual([true, false]);
    expect(['Dispatched', 'InProgress', 'Completed'].map((status) => canComplete({ status: status as never }))).toEqual([true, true, false]);
  });
});

describe('applySyncResponse', () => {
  const queued = [change('start', 'c1'), change('complete', 'c2'), change('note', 'c3')];
  const state = { ...emptyState, queue: queued };

  it("takes answered changes off the queue and keeps unanswered ones, in order", () => {
    const next = applySyncResponse(state, queued, {
      serverTime: '2026-11-25T15:00:00Z',
      results: [{ id: 'c1', outcome: 'Applied', explanation: null }],
    });

    expect(next.queue.map((c) => c.id)).toEqual(['c2', 'c3']);
    expect(next.lastSyncedAt).toBe('2026-11-25T15:00:00Z');
    expect(next.notices).toEqual([]);
  });

  it('turns every answer the technician needs to read into a notice', () => {
    const next = applySyncResponse(state, queued, {
      serverTime: '2026-11-25T15:00:00Z',
      results: [
        { id: 'c1', outcome: 'AlreadyDone', explanation: null },
        { id: 'c2', outcome: 'Rejected', explanation: 'J-24051 was given to Tyler Brooks while you were offline.' },
        { id: 'c3', outcome: 'Invalid', explanation: null },
      ],
    });

    expect(next.queue).toEqual([]);
    expect(next.notices.map((n) => n.message)).toEqual([
      'J-24051 was given to Tyler Brooks while you were offline.',
      "J-24051: this change couldn't be applied.",
    ]);
  });

  it('says one cause once: a refused start is folded into the refused completion that says more', () => {
    const next = applySyncResponse(state, queued, {
      serverTime: '2026-11-25T15:00:00Z',
      results: [
        { id: 'c1', outcome: 'Rejected', explanation: 'J-24051 was taken off your day while you were offline.' },
        {
          id: 'c2',
          outcome: 'Rejected',
          explanation: 'J-24051 was taken off your day while you were offline. Your resolution was saved as a note on the job.',
        },
        { id: 'c3', outcome: 'Applied', explanation: null },
      ],
    });

    expect(next.notices.map((n) => n.id)).toEqual(['c2']);
  });

  it('keeps notices about different jobs, even when they read the same', () => {
    const other = { ...change('start', 'c4'), jobId: 2, jobNumber: 'J-24052' };
    const next = applySyncResponse({ ...state, queue: [queued[0], other] }, [queued[0], other], {
      serverTime: '2026-11-25T15:00:00Z',
      results: [
        { id: 'c1', outcome: 'Rejected', explanation: 'Taken off your day while you were offline.' },
        { id: 'c4', outcome: 'Rejected', explanation: 'Taken off your day while you were offline.' },
      ],
    });

    expect(next.notices.map((n) => n.id)).toEqual(['c1', 'c4']);
  });

  it('shows the same answer once, even if it arrives twice', () => {
    const response = {
      serverTime: '2026-11-25T15:00:00Z',
      results: [{ id: 'c2', outcome: 'Rejected' as const, explanation: 'J-24051 was cancelled while you were offline.' }],
    };

    const twice = applySyncResponse(applySyncResponse(state, queued, response), queued, response);

    expect(twice.notices).toHaveLength(1);
  });
});

describe('the rest', () => {
  it('sends the oldest changes first, up to the limit', () => {
    expect(nextBatch([change('start', 'c1'), change('note', 'c2'), change('note', 'c3')], 2).map((c) => c.id)).toEqual(['c1', 'c2']);
  });

  it('keeps waiting changes when a fresh day arrives', () => {
    const next = applyDay(enqueue(emptyState, change('note', 'c1')), day());

    expect(next.queue).toHaveLength(1);
    expect(next.lastSyncedAt).toBe('2026-11-25T13:00:00Z');
  });
});
