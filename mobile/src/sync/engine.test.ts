import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { retryDelay, SyncEngine, type Snapshot } from './engine';
import { FakeServer, job, MemoryStore, sequentialIds } from './testing';

/**
 * The offline story, end to end inside the app: record changes with no signal, survive a
 * restart, come back online, and have each change applied exactly once.
 */
describe('SyncEngine', () => {
  let server: FakeServer;
  let store: MemoryStore;
  let last: Snapshot | null;

  beforeEach(() => {
    vi.useFakeTimers();
    server = new FakeServer();
    store = new MemoryStore();
    last = null;
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  async function engine(ids = sequentialIds()) {
    const created = new SyncEngine({
      api: server,
      store,
      token: 'trk_test',
      newId: ids,
      now: () => new Date('2026-11-25T14:30:00Z'),
      onChange: (snapshot) => (last = snapshot),
    });
    await created.load();
    return created;
  }

  it('saves a change on the phone before it tries to send it', async () => {
    let release!: () => void;
    server.gate = new Promise((resolve) => (release = resolve));
    const fieldApp = await engine();

    await fieldApp.record('note', job(), { body: 'Dog in the yard.' });

    // The server hasn't answered yet, and the note is already on disk.
    expect(store.state?.queue.map((c) => c.body)).toEqual(['Dog in the yard.']);
    release();
    await fieldApp.sync();
    expect(store.state?.queue).toEqual([]);
  });

  it('works offline, survives a restart, and sends everything when the signal comes back', async () => {
    server.offline = true;
    const before = await engine();
    await before.sync(); // No signal: nothing comes down, nothing is lost.
    await before.record('start', job());
    await before.record('complete', job(), { resolution: 'Replaced the ignitor.' });
    await before.sync();

    expect(last?.status).toBe('offline');
    expect(store.state?.queue.map((c) => c.kind)).toEqual(['start', 'complete']);
    before.stop(); // The app was closed.

    server.offline = false;
    const after = await engine(sequentialIds()); // Reopened later, with a signal.
    await after.sync();

    expect(server.jobs[0]).toMatchObject({ status: 'Completed', resolution: 'Replaced the ignitor.' });
    expect(store.state?.queue).toEqual([]);
    expect(last).toMatchObject({ status: 'idle', problem: null });
    expect(last?.view?.jobs[0]).toMatchObject({ status: 'Completed', waiting: 0 });
  });

  it('applies a change once when the answer was lost and it had to be sent again', async () => {
    const fieldApp = await engine();
    server.loseNextAnswer = true;

    await fieldApp.record('complete', job(), { resolution: 'Replaced the ignitor.' });
    await vi.waitFor(() => expect(last?.status).toBe('offline'));
    expect(store.state?.queue).toHaveLength(1); // The phone never heard back, so it kept it.

    await fieldApp.sync();

    expect(server.syncCalls).toBe(2);
    expect(server.applied.get('c1')).toBe(1);
    expect(store.state?.queue).toEqual([]);
  });

  it("explains a change the office overruled, and drops the job the office gave away", async () => {
    server.reassigned.set(1, 'Tyler Brooks');
    const fieldApp = await engine();

    await fieldApp.record('complete', job(), { resolution: 'Replaced the ignitor.' });
    await fieldApp.sync();

    expect(last?.state.notices.map((n) => n.message)).toEqual(['J-24051 was given to Tyler Brooks while you were offline.']);
    expect(last?.view?.jobs).toEqual([]);

    await fieldApp.dismiss('c1');
    expect(store.state?.notices).toEqual([]);
  });

  it('keeps the queue and stops retrying when the phone was signed out on the web', async () => {
    server.revoked = true;
    const fieldApp = await engine();

    await fieldApp.record('note', job(), { body: 'Dog in the yard.' });
    await fieldApp.sync();
    vi.advanceTimersByTime(10 * 60_000);

    expect(last).toMatchObject({ status: 'signed-out', problem: 'Sign in again on this phone.' });
    expect(store.state?.queue).toHaveLength(1);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('tries again by itself, backing off, until the signal is back', async () => {
    server.offline = true;
    const fieldApp = await engine();
    await fieldApp.record('note', job(), { body: 'Dog in the yard.' });
    await vi.waitFor(() => expect(last?.status).toBe('offline'));

    server.offline = false;
    await vi.advanceTimersByTimeAsync(retryDelay(1));

    expect(last?.status).toBe('idle');
    expect(store.state?.queue).toEqual([]);
  });

  it('joins a sync already running instead of sending the same batch twice at once', async () => {
    let release!: () => void;
    server.gate = new Promise((resolve) => (release = resolve));
    const fieldApp = await engine();
    await fieldApp.record('note', job(), { body: 'One.' }); // Starts a sync, held at the gate.

    const second = fieldApp.sync();
    const third = fieldApp.sync();
    release();
    await Promise.all([second, third]);

    expect(server.syncCalls).toBe(1);
    expect(server.applied.get('c1')).toBe(1);
  });

  it('waits 2, 4, 8 seconds and so on between tries, never more than a minute', () => {
    expect([1, 2, 3, 4, 5, 6, 10].map(retryDelay)).toEqual([2_000, 4_000, 8_000, 16_000, 32_000, 60_000, 60_000]);
  });
});
