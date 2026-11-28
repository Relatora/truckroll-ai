import { ApiError, type FieldApi } from '../api/client';
import type { FieldChangeRequest, FieldChangeResult, FieldDay, FieldJob } from '../api/types';
import type { StateStore } from './engine';
import type { FieldState } from './queue';

/** Test helpers: a day, a fake server and an in-memory store. Never imported by the app. */

export function job(overrides: Partial<FieldJob> = {}): FieldJob {
  return {
    id: 1,
    number: 'J-24051',
    status: 'Dispatched',
    type: 'Repair',
    priority: 'Normal',
    windowStart: '2026-11-25T14:00:00Z',
    windowEnd: '2026-11-25T16:00:00Z',
    estimatedMinutes: 90,
    summary: 'No heat.',
    resolution: null,
    customer: { name: 'Priya Shah', phone: '519-555-0142' },
    place: { street: '14 Maple St', city: 'Guelph', accessNotes: 'Side door.', latitude: 43.55, longitude: -80.25 },
    equipment: null,
    history: [],
    notes: [],
    ...overrides,
  };
}

export function day(jobs: FieldJob[] = [job()]): FieldDay {
  return { date: '2026-11-25', serverTime: '2026-11-25T13:00:00Z', technician: 'Nadia Kowalczyk', jobs };
}

/**
 * A stand-in for the field API that keeps the server's one promise the app relies on: a change
 * id it has seen gets the same answer again and is never applied twice.
 */
export class FakeServer implements FieldApi {
  jobs: FieldJob[];
  /** Answers by change id, like the FieldChanges table. */
  readonly answers = new Map<string, FieldChangeResult>();
  /** How many times each change id actually changed something. */
  readonly applied = new Map<string, number>();
  offline = false;
  revoked = false;
  /** Apply the next sync, then fail to answer, as if the signal dropped on the way back. */
  loseNextAnswer = false;
  syncCalls = 0;
  /** Set to hold sync calls open until released. */
  gate: Promise<void> | null = null;
  /** Who the office gave each job to while the phone was away, by job id. */
  reassigned = new Map<number, string>();

  constructor(jobs: FieldJob[] = [job()]) {
    this.jobs = jobs;
  }

  async signIn(): Promise<never> {
    throw new Error('Not used in these tests.');
  }

  async getDay(): Promise<FieldDay> {
    this.check();
    return { ...day(this.jobs.filter((j) => !this.reassigned.has(j.id))), serverTime: '2026-11-25T15:00:00Z' };
  }

  async sync(_token: string, changes: FieldChangeRequest[]) {
    this.syncCalls += 1;
    if (this.gate) await this.gate;
    this.check();

    const results = changes.map((change) => this.answers.get(change.id) ?? this.apply(change));
    if (this.loseNextAnswer) {
      this.loseNextAnswer = false;
      throw new ApiError('offline', 'The answer never arrived.');
    }
    return { serverTime: '2026-11-25T15:00:00Z', results };
  }

  private apply(change: FieldChangeRequest): FieldChangeResult {
    const target = this.jobs.find((j) => j.id === change.jobId)!;
    const other = this.reassigned.get(change.jobId);
    let result: FieldChangeResult;

    if (change.kind !== 'note' && other) {
      result = { id: change.id, outcome: 'Rejected', explanation: `${target.number} was given to ${other} while you were offline.` };
    } else {
      if (change.kind === 'start') target.status = 'InProgress';
      if (change.kind === 'complete') {
        target.status = 'Completed';
        target.resolution = change.resolution;
      }
      if (change.kind === 'note') {
        target.notes = [...target.notes, { author: 'Nadia Kowalczyk', source: 'Technician', body: change.body!, createdAt: change.madeAt }];
      }
      this.applied.set(change.id, (this.applied.get(change.id) ?? 0) + 1);
      result = { id: change.id, outcome: 'Applied', explanation: null };
    }

    this.answers.set(change.id, result);
    return result;
  }

  private check() {
    if (this.offline) throw new ApiError('offline', "Can't reach Truckroll.");
    if (this.revoked) throw new ApiError('unauthorized', 'Sign in again on this phone.');
  }
}

/** Keeps state in memory, as JSON, so nothing is shared by reference with the engine. */
export class MemoryStore implements StateStore {
  saved: string | null = null;
  saves = 0;

  async load(): Promise<FieldState | null> {
    return this.saved ? (JSON.parse(this.saved) as FieldState) : null;
  }

  async save(state: FieldState): Promise<void> {
    this.saves += 1;
    this.saved = JSON.stringify(state);
  }

  /** What's on "disk" right now. */
  get state(): FieldState | null {
    return this.saved ? (JSON.parse(this.saved) as FieldState) : null;
  }
}

/** Ids c1, c2, c3 ... so tests can name the changes they made. */
export function sequentialIds() {
  let next = 0;
  return () => `c${++next}`;
}
