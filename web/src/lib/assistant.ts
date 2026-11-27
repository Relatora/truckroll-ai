"use client";

import { useCallback, useRef, useState } from "react";
import { ApiError } from "./api";

/** Mirrors the records in Truckroll.Api/Features/Assistant/AssistantEndpoints.cs. */

export type PassageSource = "Document" | "Equipment" | "Job";

export type Citation = { number: number; record: string; source: PassageSource; title: string; text: string; link: string | null };

export type AnswerDone = { model: string | null; provider: string; milliseconds: number; inputTokens: number | null; outputTokens: number | null; grounded: boolean };

export type TurnStatus = "searching" | "writing" | "done" | "stopped" | "error";

/** One question and its answer, as it builds up. */
export type Turn = {
  id: string;
  question: string;
  sources: Citation[] | null;
  answer: string;
  status: TurnStatus;
  done?: AnswerDone;
  error?: string;
};

type ServerEvent =
  | { type: "sources"; data: Citation[] }
  | { type: "delta"; data: { text: string } }
  | { type: "done"; data: AnswerDone }
  | { type: "error"; data: { message: string } };

/**
 * POSTs a question and yields the server-sent events as they arrive. EventSource can't POST,
 * so this reads the stream itself: events are separated by a blank line, each with an
 * "event:" line and one or more "data:" lines.
 */
async function* ask(question: string, history: { role: "user" | "assistant"; text: string }[], signal: AbortSignal): AsyncGenerator<ServerEvent> {
  const response = await fetch("/api/assistant/ask", {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "text/event-stream" },
    body: JSON.stringify({ question, history }),
    signal,
  });

  if (!response.ok || !response.body) {
    let message = response.statusText;
    try {
      const problem = (await response.json()) as { title?: string; errors?: Record<string, string[]> };
      message = Object.values(problem.errors ?? {})[0]?.[0] ?? problem.title ?? message;
    } catch {
      // Not JSON; keep the status text.
    }
    throw new ApiError(response.status, message);
  }

  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
  let buffer = "";
  while (true) {
    const { value, done } = await reader.read();
    if (done) return;
    buffer += value.replace(/\r\n/g, "\n");

    let end: number;
    while ((end = buffer.indexOf("\n\n")) >= 0) {
      const block = buffer.slice(0, end);
      buffer = buffer.slice(end + 2);
      const type = block.match(/^event: (.+)$/m)?.[1];
      const data = block
        .split("\n")
        .filter((line) => line.startsWith("data: "))
        .map((line) => line.slice(6))
        .join("\n");
      if (type && data) yield { type, data: JSON.parse(data) } as ServerEvent;
    }
  }
}

/**
 * The conversation on the assistant page. It lives in the page, not the server: closing the tab
 * ends it, which is the honest behaviour for v0.1 (no memory across sessions, issue #13).
 */
export function useAssistant() {
  const [turns, setTurns] = useState<Turn[]>([]);
  const controller = useRef<AbortController | null>(null);

  const update = useCallback((id: string, change: (turn: Turn) => Partial<Turn>) => {
    setTurns((all) => all.map((turn) => (turn.id === id ? { ...turn, ...change(turn) } : turn)));
  }, []);

  const busy = turns.some((t) => t.status === "searching" || t.status === "writing");

  const send = useCallback(
    async (question: string) => {
      if (busy || !question.trim()) return;
      const id = crypto.randomUUID();
      // Only finished exchanges go back as history; a stopped or failed answer would mislead.
      const history = turns
        .filter((t) => t.status === "done")
        .flatMap((t) => [
          { role: "user" as const, text: t.question },
          { role: "assistant" as const, text: t.answer },
        ]);

      setTurns((all) => [...all, { id, question: question.trim(), sources: null, answer: "", status: "searching" }]);
      controller.current = new AbortController();

      try {
        for await (const event of ask(question.trim(), history.slice(-12), controller.current.signal)) {
          switch (event.type) {
            case "sources":
              update(id, () => ({ sources: event.data, status: "writing" }));
              break;
            case "delta":
              update(id, (turn) => ({ answer: turn.answer + event.data.text }));
              break;
            case "done":
              update(id, () => ({ done: event.data, status: "done" }));
              break;
            case "error":
              update(id, () => ({ error: event.data.message, status: "error" }));
              break;
          }
        }
      } catch (error) {
        if (controller.current?.signal.aborted) {
          update(id, () => ({ status: "stopped" }));
        } else {
          update(id, () => ({ status: "error", error: error instanceof ApiError ? error.message : "Couldn't reach Truckroll. Check your connection and try again." }));
        }
      } finally {
        // A stream that ended without "done" (the server restarted, say) shouldn't spin forever.
        update(id, (turn) => (turn.status === "searching" || turn.status === "writing" ? { status: "error", error: "The answer was cut off. Try again." } : {}));
        controller.current = null;
      }
    },
    [busy, turns, update],
  );

  const stop = useCallback(() => controller.current?.abort(), []);
  const reset = useCallback(() => {
    controller.current?.abort();
    setTurns([]);
  }, []);

  return { turns, busy, send, stop, reset };
}
