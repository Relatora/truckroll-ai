import type { FieldChangeRequest, FieldDay, FieldSignInRequest, FieldSignInResponse, FieldSyncResponse } from './types';

/**
 * Why a call to the API failed, in the terms the app acts on:
 *
 * - `offline`: no answer at all (no signal, server unreachable, timed out). Keep the queue, try later.
 * - `unauthorized`: the token was revoked or expired. Keep the queue, ask them to sign in again.
 * - `forbidden`: signed in fine, but not a technician (sign-in only).
 * - `rejected`: the server read the request and refused it (a 4xx). Retrying won't help.
 * - `server`: the server failed (a 5xx). Try later.
 */
export type ApiErrorKind = 'offline' | 'unauthorized' | 'forbidden' | 'rejected' | 'server';

export class ApiError extends Error {
  constructor(
    readonly kind: ApiErrorKind,
    message: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

/** What the sync engine needs from the API. The tests give it a fake. */
export interface FieldApi {
  signIn(request: FieldSignInRequest): Promise<FieldSignInResponse>;
  getDay(token: string): Promise<FieldDay>;
  sync(token: string, changes: FieldChangeRequest[]): Promise<FieldSyncResponse>;
}

/** Long enough for a weak signal, short enough that a dead one doesn't hang the sync. */
const TimeoutMs = 20_000;

/**
 * The real API over fetch. `baseUrl` is the web app's origin: it proxies /api to the API
 * (docs/adr/006), so a phone needs one address, the same one people use in a browser.
 */
export function createFieldApi(baseUrl: string, fetchImpl?: typeof fetch): FieldApi {
  async function call<T>(path: string, init: RequestInit & { token?: string }): Promise<T> {
    const headers: Record<string, string> = { Accept: 'application/json' };
    if (init.body) headers['Content-Type'] = 'application/json';
    if (init.token) headers.Authorization = `Bearer ${init.token}`;

    const abort = new AbortController();
    const timer = setTimeout(() => abort.abort(), TimeoutMs);
    let response: Response;
    try {
      // Looked up per call, not captured once, so whatever fetch is current is the one used.
      response = await (fetchImpl ?? globalThis.fetch)(`${baseUrl}/api/field${path}`, { ...init, headers, signal: abort.signal });
    } catch {
      // fetch only throws when there was no HTTP answer at all.
      throw new ApiError('offline', "Can't reach Truckroll. Changes are kept on this phone.");
    } finally {
      clearTimeout(timer);
    }

    if (response.ok) {
      return (await response.json()) as T;
    }

    const problem = await readProblem(response);
    if (response.status === 401) throw new ApiError('unauthorized', problem ?? 'Sign in again on this phone.');
    if (response.status === 403) throw new ApiError('forbidden', problem ?? "This account can't use the field app.");
    if (response.status >= 500) throw new ApiError('server', 'Truckroll had a problem. It will try again shortly.');
    throw new ApiError('rejected', problem ?? `The request was refused (${response.status}).`);
  }

  return {
    signIn: (request) => call('/sign-in', { method: 'POST', body: JSON.stringify(request) }),
    getDay: (token) => call('/day', { method: 'GET', token }),
    sync: (token, changes) => call('/sync', { method: 'POST', token, body: JSON.stringify({ changes }) }),
  };
}

/** The useful sentence from an RFC 7807 problem body, if there is one. */
async function readProblem(response: Response): Promise<string | null> {
  try {
    const body = (await response.json()) as { detail?: string; title?: string; errors?: Record<string, string[]> };
    const firstError = body.errors ? Object.values(body.errors).flat()[0] : undefined;
    return body.detail ?? firstError ?? body.title ?? null;
  } catch {
    return null;
  }
}
