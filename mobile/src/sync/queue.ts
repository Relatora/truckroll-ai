import type {
  FieldChangeKind,
  FieldChangeRequest,
  FieldDay,
  FieldJob,
  FieldNote,
  FieldSyncResponse,
} from '../api/types';

/**
 * The offline model, as pure functions: no storage, no network, no clock. The sync engine
 * (engine.ts) does the I/O and calls these, and the tests run them without a phone.
 *
 * Two rules shape it:
 *
 * 1. **The server's copy is never edited.** `day` holds exactly what the API last returned.
 *    What the technician sees is that copy with their waiting changes replayed on top
 *    (`projectDay`). When the server answers, the replay simply has less to do, and nothing
 *    has to be undone if the office won a conflict.
 * 2. **Nothing typed is ever thrown away.** A change leaves the queue only when the server has
 *    answered for it. If the answer is "no", the technician gets the server's explanation as a
 *    notice, and the server has already kept their text as a note (docs/adr/011).
 */

/** A change waiting to be sent: the request itself, plus the job number for messages. */
export interface QueuedChange extends FieldChangeRequest {
  jobNumber: string;
}

/** Something the technician should read: a change of theirs that didn't apply as made. */
export interface Notice {
  /** The change it's about, so the same answer never shows twice. */
  id: string;
  jobId: number;
  jobNumber: string;
  message: string;
}

/** Everything the app keeps on the phone between launches. */
export interface FieldState {
  /** The server's copy of the day, exactly as it came. */
  day: FieldDay | null;
  /** Changes not yet answered by the server, oldest first. */
  queue: QueuedChange[];
  notices: Notice[];
  /** The server's clock the last time it answered: how fresh `day` is. */
  lastSyncedAt: string | null;
}

export const emptyState: FieldState = { day: null, queue: [], notices: [], lastSyncedAt: null };

/** The server's limit on one sync (FieldEndpoints.MaxChangesPerSync). */
export const MaxChangesPerSync = 200;

// ------------------------------------------------------------------ making changes

/** What the technician entered for a change. */
export interface ChangeInput {
  body?: string;
  resolution?: string;
  firstTimeFix?: boolean;
}

/**
 * Builds a change, or says why it can't be made. The checks match the server's, so a change the
 * phone accepts is one the server will understand, hours later, with nobody there to fix it.
 */
export function makeChange(
  kind: FieldChangeKind,
  job: Pick<FieldJob, 'id' | 'number'>,
  input: ChangeInput,
  id: string,
  madeAt: Date,
): { change: QueuedChange } | { error: string } {
  const body = input.body?.trim() ?? '';
  const resolution = input.resolution?.trim() ?? '';

  if (kind === 'note' && body === '') {
    return { error: 'Write the note first.' };
  }
  if (kind === 'complete' && resolution === '') {
    return { error: 'Say what you found and what you did.' };
  }

  return {
    change: {
      id,
      jobId: job.id,
      jobNumber: job.number,
      kind,
      madeAt: madeAt.toISOString(),
      body: kind === 'note' ? body : null,
      resolution: kind === 'complete' ? resolution : null,
      firstTimeFix: kind === 'complete' ? (input.firstTimeFix ?? true) : null,
    },
  };
}

export function enqueue(state: FieldState, change: QueuedChange): FieldState {
  return { ...state, queue: [...state.queue, change] };
}

// ------------------------------------------------------------------ what the technician sees

/** A note on screen: the server's, or one of theirs still waiting to be sent. */
export interface ViewNote extends FieldNote {
  waiting: boolean;
}

/** A job as the technician sees it: the server's copy with their waiting changes applied. */
export interface ViewJob extends Omit<FieldJob, 'notes'> {
  notes: ViewNote[];
  /** How many of their changes to this job haven't reached the server yet. */
  waiting: number;
}

export interface ViewDay extends Omit<FieldDay, 'jobs'> {
  jobs: ViewJob[];
}

/**
 * Replays the waiting changes over the server's copy, the way the server will apply them if
 * nothing has changed at the office. If something has, the server's answer wins when it comes.
 */
export function projectDay(day: FieldDay, queue: readonly QueuedChange[]): ViewDay {
  return {
    ...day,
    jobs: day.jobs.map((job) => {
      const view: ViewJob = {
        ...job,
        notes: job.notes.map((note) => ({ ...note, waiting: false })),
        waiting: 0,
      };

      for (const change of queue) {
        if (change.jobId !== job.id) continue;
        view.waiting += 1;

        if (change.kind === 'start' && view.status === 'Dispatched') {
          view.status = 'InProgress';
        } else if (change.kind === 'complete' && canComplete(view)) {
          view.status = 'Completed';
          view.resolution = change.resolution;
        } else if (change.kind === 'note') {
          view.notes = [
            ...view.notes,
            { author: day.technician, source: 'Technician', body: change.body ?? '', createdAt: change.madeAt, waiting: true },
          ];
        }
      }

      return view;
    }),
  };
}

/** Start is offered only for a job that's been sent to them and not yet started. */
export const canStart = (job: Pick<FieldJob, 'status'>) => job.status === 'Dispatched';

/** Complete is offered straight from dispatched too: forgetting to tap Start mustn't block them. */
export const canComplete = (job: Pick<FieldJob, 'status'>) => job.status === 'Dispatched' || job.status === 'InProgress';

// ------------------------------------------------------------------ the server's answer

/** The changes to send next: the oldest first, up to the server's limit. */
export function nextBatch(queue: readonly QueuedChange[], max = MaxChangesPerSync): QueuedChange[] {
  return queue.slice(0, max);
}

/**
 * Takes the server's answer to a batch. Each change it answered leaves the queue; any it didn't
 * answer stays, in order, for the next sync. Answers the technician needs to read become notices.
 */
export function applySyncResponse(state: FieldState, sent: readonly QueuedChange[], response: FieldSyncResponse): FieldState {
  const answered = new Map(response.results.map((result) => [result.id, result]));
  const notices = [...state.notices];

  for (const change of sent) {
    const result = answered.get(change.id);
    if (!result || notices.some((notice) => notice.id === change.id)) continue;

    const message =
      result.outcome === 'Rejected' || result.outcome === 'Invalid'
        ? (result.explanation ?? `${change.jobNumber}: this change couldn't be applied.`)
        : result.explanation; // AlreadyDone can carry one ("already completed"); Applied never does.

    if (message) {
      notices.push({ id: change.id, jobId: change.jobId, jobNumber: change.jobNumber, message });
    }
  }

  return {
    ...state,
    queue: state.queue.filter((change) => !answered.has(change.id)),
    notices: withoutRepeats(notices),
    lastSyncedAt: response.serverTime,
  };
}

/**
 * One cause, one message. A start and a completion refused because the office took the job back
 * both say "J-24051 was taken off your day"; the completion's answer also says the resolution
 * was kept. Show only that one: drop any notice that another for the same job already says in
 * full.
 */
function withoutRepeats(notices: Notice[]): Notice[] {
  return notices.filter(
    (notice, index) =>
      !notices.some(
        (other, otherIndex) =>
          other.jobId === notice.jobId &&
          otherIndex !== index &&
          other.message.startsWith(notice.message) &&
          (other.message.length > notice.message.length || otherIndex < index),
      ),
  );
}

/** A fresh copy of the day. Waiting changes stay queued and are replayed over it. */
export function applyDay(state: FieldState, day: FieldDay): FieldState {
  return { ...state, day, lastSyncedAt: day.serverTime };
}

export function dismissNotice(state: FieldState, id: string): FieldState {
  return { ...state, notices: state.notices.filter((notice) => notice.id !== id) };
}
