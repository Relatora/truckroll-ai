import { ApiError, type FieldApi } from '../api/client';
import type { FieldChangeKind, FieldJob } from '../api/types';
import {
  applyDay,
  applySyncResponse,
  dismissNotice,
  emptyState,
  enqueue,
  makeChange,
  nextBatch,
  projectDay,
  type ChangeInput,
  type FieldState,
  type ViewDay,
} from './queue';

/**
 * Where syncing stands, for the bar at the top of the day.
 *
 * - `idle`: everything sent, or nothing to send.
 * - `syncing`: talking to the server now.
 * - `offline`: no answer from the server. Changes wait on the phone; it tries again by itself.
 * - `signed-out`: the server no longer accepts this phone's token. Changes wait for a sign-in.
 * - `error`: the server failed. It tries again by itself.
 */
export type SyncStatus = 'idle' | 'syncing' | 'offline' | 'signed-out' | 'error';

export interface Snapshot {
  state: FieldState;
  view: ViewDay | null;
  status: SyncStatus;
  /** The last failure, in words for the technician. */
  problem: string | null;
}

/** Keeps the phone's copy between launches. On a phone it's AsyncStorage; in tests, memory. */
export interface StateStore {
  load(): Promise<FieldState | null>;
  save(state: FieldState): Promise<void>;
}

export interface EngineDeps {
  api: FieldApi;
  store: StateStore;
  token: string;
  /** Makes change ids. UUIDs on the phone; predictable ones in tests. */
  newId: () => string;
  now: () => Date;
  /** Called after every change of state, already saved. */
  onChange?: (snapshot: Snapshot) => void;
}

/** How long to wait before trying again after `failures` failed syncs in a row: 2s, 4s, 8s ... 60s. */
export function retryDelay(failures: number): number {
  return Math.min(60_000, 2_000 * 2 ** Math.max(0, failures - 1));
}

/**
 * Runs the offline queue: records changes, sends them when it can, and takes the day from the
 * server. The pure rules live in queue.ts; this is the part with I/O.
 *
 * The order of every operation is chosen so a phone can die at any moment without losing
 * anything: a change is saved before the app says it's recorded, and leaves the queue only
 * after the server has answered for it. If the answer is lost on the way back, the change is
 * sent again, and the server answers it again without applying it twice (its id is recorded).
 */
export class SyncEngine {
  private state: FieldState = emptyState;
  private status: SyncStatus = 'idle';
  private problem: string | null = null;
  private running: Promise<void> | null = null;
  private again = false;
  private failures = 0;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private stopped = false;

  constructor(private readonly deps: EngineDeps) {}

  /** Loads what the phone kept from last time. Call before anything else. */
  async load(): Promise<void> {
    this.state = (await this.deps.store.load()) ?? emptyState;
    this.publish();
  }

  snapshot(): Snapshot {
    return {
      state: this.state,
      view: this.state.day ? projectDay(this.state.day, this.state.queue) : null,
      status: this.status,
      problem: this.problem,
    };
  }

  /**
   * Records something the technician did. It's saved on the phone before this returns, then
   * sent in the background. Returns an error message instead if the input isn't complete.
   */
  async record(kind: FieldChangeKind, job: Pick<FieldJob, 'id' | 'number'>, input: ChangeInput = {}): Promise<string | null> {
    const made = makeChange(kind, job, input, this.deps.newId(), this.deps.now());
    if ('error' in made) return made.error;

    await this.commit(enqueue(this.state, made.change));
    void this.sync();
    return null;
  }

  async dismiss(noticeId: string): Promise<void> {
    await this.commit(dismissNotice(this.state, noticeId));
  }

  /**
   * Sends everything waiting, then takes a fresh copy of the day. Safe to call at any time and
   * as often as you like: calls while a sync is running join it, and it runs once more after.
   */
  sync(): Promise<void> {
    if (this.stopped) return Promise.resolve();
    if (this.running) {
      this.again = true;
      return this.running;
    }

    this.running = this.run().finally(() => {
      this.running = null;
    });
    return this.running;
  }

  /** Stops retrying. The engine is done; a sign-out or a new token makes a new one. */
  stop(): void {
    this.stopped = true;
    if (this.retryTimer) clearTimeout(this.retryTimer);
  }

  private async run(): Promise<void> {
    if (this.retryTimer) {
      clearTimeout(this.retryTimer);
      this.retryTimer = null;
    }

    do {
      this.again = false;
      this.setStatus('syncing', null);
      try {
        // Send first, then read: the fresh day then already includes what was just sent.
        while (this.state.queue.length > 0) {
          const batch = nextBatch(this.state.queue);
          const response = await this.deps.api.sync(this.deps.token, batch);
          await this.commit(applySyncResponse(this.state, batch, response));
          if (response.results.length === 0) break; // Nothing answered: don't spin.
        }

        await this.commit(applyDay(this.state, await this.deps.api.getDay(this.deps.token)));
        this.failures = 0;
        this.setStatus('idle', null);
      } catch (error) {
        this.fail(error);
        return;
      }
    } while (this.again && !this.stopped);
  }

  private fail(error: unknown): void {
    const apiError = error instanceof ApiError ? error : new ApiError('server', 'Something went wrong. It will try again shortly.');

    if (apiError.kind === 'unauthorized') {
      // No retry: the token won't start working again. The queue stays for the next sign-in.
      this.setStatus('signed-out', apiError.message);
      return;
    }

    this.setStatus(apiError.kind === 'offline' ? 'offline' : 'error', apiError.message);
    this.failures += 1;
    if (!this.stopped) {
      this.retryTimer = setTimeout(() => void this.sync(), retryDelay(this.failures));
    }
  }

  /** Saves, then tells the screen. In that order: what's on screen is always on disk too. */
  private async commit(next: FieldState): Promise<void> {
    this.state = next;
    await this.deps.store.save(next);
    this.publish();
  }

  private setStatus(status: SyncStatus, problem: string | null): void {
    this.status = status;
    this.problem = problem;
    this.publish();
  }

  private publish(): void {
    this.deps.onChange?.(this.snapshot());
  }
}
