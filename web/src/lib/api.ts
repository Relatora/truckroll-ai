/**
 * A thin wrapper over fetch for the Truckroll API. Every call goes to the same origin (/api is
 * rewritten to the ASP.NET Core API), so the session cookie rides along automatically.
 *
 * Failures throw ApiError with the status. A 401 is handled once, globally, in Providers.
 */

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api${path}`, {
    ...init,
    headers: { "Content-Type": "application/json", ...init?.headers },
  });

  if (!response.ok) {
    throw new ApiError(response.status, await errorMessage(response));
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

async function errorMessage(response: Response): Promise<string> {
  try {
    // ASP.NET Core answers with RFC 9457 problem details.
    const problem = (await response.json()) as { title?: string; detail?: string };
    return problem.detail ?? problem.title ?? response.statusText;
  } catch {
    return response.statusText;
  }
}
