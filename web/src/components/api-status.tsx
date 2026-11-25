"use client";

import { useEffect, useState } from "react";

/** The shape of GET /api/status (Truckroll.Api/Features/Status/StatusEndpoints.cs). */
type Status = {
  service: string;
  version: string;
  status: string;
  dependencies: { name: string; status: string }[];
};

type State = { kind: "loading" } | { kind: "ok"; status: Status } | { kind: "error"; message: string };

/**
 * Shows whether the API and its dependencies answer, by calling /api/status through the
 * same-origin rewrite. If this says "connected", the whole chain works: browser, Next.js,
 * the rewrite, the API, MySQL and Redis.
 */
export function ApiStatus() {
  const [state, setState] = useState<State>({ kind: "loading" });

  useEffect(() => {
    const controller = new AbortController();

    fetch("/api/status", { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error(`API answered ${response.status}`);
        setState({ kind: "ok", status: (await response.json()) as Status });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setState({ kind: "error", message: error instanceof Error ? error.message : "API unreachable" });
      });

    return () => controller.abort();
  }, []);

  if (state.kind === "loading") {
    return <p className="text-sm text-muted-foreground">Checking the API…</p>;
  }

  if (state.kind === "error") {
    return (
      <p role="alert" className="text-sm text-destructive">
        API not reachable: {state.message}
      </p>
    );
  }

  const { status } = state;
  return (
    <div className="space-y-2 text-sm" aria-live="polite">
      <p>
        API connected: <span className="font-medium">{status.service}</span> {status.version.split("+")[0]},{" "}
        {status.status.toLowerCase()}
      </p>
      <ul className="flex flex-wrap gap-2">
        {status.dependencies.map((dependency) => (
          <li key={dependency.name} className="rounded-md border px-2 py-0.5 font-mono text-xs">
            {dependency.name}: {dependency.status.toLowerCase()}
          </li>
        ))}
      </ul>
    </div>
  );
}
