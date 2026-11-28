import { describe, expect, it } from 'vitest';
import { ApiError, createFieldApi } from './client';

/** A fetch that answers with one status and body, and remembers what it was asked. */
function answering(status: number, body: unknown = null) {
  const calls: { url: string; init: RequestInit }[] = [];
  const fetchImpl = (async (url: string, init: RequestInit) => {
    calls.push({ url, init });
    return new Response(body === null ? null : JSON.stringify(body), { status });
  }) as unknown as typeof fetch;
  return { calls, fetchImpl };
}

async function failure(promise: Promise<unknown>): Promise<ApiError> {
  try {
    await promise;
  } catch (error) {
    if (error instanceof ApiError) return error;
  }
  throw new Error('Expected an ApiError.');
}

describe('createFieldApi', () => {
  it('sends the token and the changes to the sync endpoint', async () => {
    const { calls, fetchImpl } = answering(200, { serverTime: 'now', results: [] });

    await createFieldApi('http://truckroll.test', fetchImpl).sync('trk_abc', []);

    expect(calls[0].url).toBe('http://truckroll.test/api/field/sync');
    expect(calls[0].init.headers).toMatchObject({ Authorization: 'Bearer trk_abc', 'Content-Type': 'application/json' });
    expect(calls[0].init.body).toBe('{"changes":[]}');
  });

  it('calls no answer at all "offline", which keeps the queue and retries', async () => {
    const api = createFieldApi('http://truckroll.test', (() => Promise.reject(new TypeError('Failed to fetch'))) as typeof fetch);

    expect((await failure(api.getDay('trk_abc'))).kind).toBe('offline');
  });

  it("names each kind of refusal the way the app acts on it, with the server's own words", async () => {
    const kinds = await Promise.all(
      [
        answering(401),
        answering(403, { title: 'The field app is for technicians', detail: 'Sign in on the web instead.' }),
        answering(400, { errors: { changes: ['Send between 1 and 200 changes.'] } }),
        answering(503),
      ].map(async ({ fetchImpl }) => {
        const error = await failure(createFieldApi('http://truckroll.test', fetchImpl).getDay('trk_abc'));
        return [error.kind, error.message];
      }),
    );

    expect(kinds).toEqual([
      ['unauthorized', 'Sign in again on this phone.'],
      ['forbidden', 'Sign in on the web instead.'],
      ['rejected', 'Send between 1 and 200 changes.'],
      ['server', 'Truckroll had a problem. It will try again shortly.'],
    ]);
  });
});
