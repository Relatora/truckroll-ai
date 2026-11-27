"use client";

import { AnimatePresence, motion } from "motion/react";
import { ArrowUp, Check, Copy, RotateCcw, Sparkles, Square } from "lucide-react";
import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { toast } from "sonner";
import { CitationDrawer, sourceKinds } from "@/components/assistant/citation-drawer";
import { RichText } from "@/components/assistant/rich-text";
import { PageHeader } from "@/components/shell/page-header";
import { useAssistant, type Citation, type Turn } from "@/lib/assistant";
import { useMe, type Role } from "@/lib/session";
import { cn } from "@/lib/utils";

/** Questions that show what the assistant is for, matched to what each role actually asks. */
const SUGGESTIONS: Record<Role, string[]> = {
  Owner: ["Which furnace models keep failing?", "Which CAL-80 furnaces are covered by bulletin SB-2024-07?", "What did we install at 14 Maple St?", "Does Comfort Club Plus waive the diagnostic fee?"],
  BranchManager: ["What boiler is at 88 Birch Cres?", "The same part failed three times on one unit. What's the process?", "How long do we guarantee our installation work?", "Which furnace models keep failing?"],
  Dispatcher: ["What counts as an emergency call?", "Can a G3 technician attend a gas emergency?", "What did we install at 14 Maple St?", "How fast must we be on site for an emergency?"],
  Csr: ["A customer smells gas. What do I tell them?", "How much is the after-hours fee?", "What did we install at 14 Maple St?", "When does a membership lapse for missed payments?"],
  Technician: ["Burners light then shut off after a few seconds", "What should a good CAL-80 ignitor read in ohms?", "How do I descale a tankless water heater?", "Who is allowed to recover refrigerant?"],
};

/**
 * Ask Truckroll: questions answered from the manuals, policies and job history the person may
 * see, with every claim numbered back to its source.
 */
export function AssistantView() {
  const { data: me } = useMe();
  const { turns, busy, send, stop, reset } = useAssistant();
  const [open, setOpen] = useState<Citation | null>(null);
  const [draft, setDraft] = useState("");
  const input = useRef<HTMLTextAreaElement>(null);
  const end = useRef<HTMLDivElement>(null);
  const last = turns.at(-1);

  // Follow the answer down the page while it's being written, unless the reader scrolls up to
  // read something else; asking again starts following again.
  const follow = useRef(true);
  useEffect(() => {
    const stopFollowing = (event: WheelEvent | TouchEvent) => {
      if (!("deltaY" in event) || event.deltaY < 0) follow.current = false;
    };
    window.addEventListener("wheel", stopFollowing, { passive: true });
    window.addEventListener("touchmove", stopFollowing, { passive: true });
    return () => {
      window.removeEventListener("wheel", stopFollowing);
      window.removeEventListener("touchmove", stopFollowing);
    };
  }, []);

  useEffect(() => {
    if (last?.status === "searching") follow.current = true;
    if (follow.current && last) end.current?.scrollIntoView({ block: "end" });
  }, [last, last?.answer, last?.status, last?.sources]);

  // Back to the box when an answer finishes, ready for the follow-up.
  useEffect(() => {
    if (last && last.status !== "searching" && last.status !== "writing") input.current?.focus();
  }, [last?.status, last]);

  function submit(event?: FormEvent) {
    event?.preventDefault();
    if (!draft.trim() || busy) return;
    void send(draft);
    setDraft("");
  }

  function onKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) submit(event);
  }

  return (
    <div className="mx-auto flex min-h-[calc(100dvh-9rem)] w-full max-w-3xl flex-col">
      <PageHeader
        title="Ask Truckroll"
        description="Answers from the manuals, policies and job history you can see. Every claim is numbered back to its source."
        actions={
          turns.length > 0 && (
            <button type="button" onClick={reset} className="glass inline-flex h-9 items-center gap-1.5 rounded-full px-3.5 text-sm font-medium hover:bg-foreground/[0.04]">
              <RotateCcw className="size-3.5" /> New conversation
            </button>
          )
        }
      />

      <div className="flex-1 space-y-10 pb-8" aria-busy={busy}>
        {turns.length === 0 ? (
          <Suggestions questions={me ? SUGGESTIONS[me.role] : []} onPick={(q) => void send(q)} />
        ) : (
          turns.map((turn) => <Exchange key={turn.id} turn={turn} onCite={setOpen} onRetry={() => void send(turn.question)} canRetry={!busy} />)
        )}
        {/* Scrolling to here leaves room for the sticky question box below it. */}
        <div ref={end} className="scroll-mb-28 max-md:scroll-mb-48" />
      </div>

      <form onSubmit={submit} className="glass-strong sticky bottom-4 z-20 flex items-end gap-2 rounded-[1.75rem] p-2 pl-5 max-md:bottom-[5.25rem]">
        <label htmlFor="question" className="sr-only">
          Your question
        </label>
        <textarea
          id="question"
          ref={input}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={onKeyDown}
          rows={1}
          maxLength={1000}
          placeholder={turns.length ? "Ask a follow-up" : "Ask about a customer, a unit, a job or a policy"}
          className="field-sizing-content max-h-[40vh] min-h-11 flex-1 resize-none bg-transparent py-2.5 text-[0.95rem] outline-none placeholder:text-muted-foreground"
        />
        {busy ? (
          <button type="button" onClick={stop} aria-label="Stop the answer" className="flex size-11 shrink-0 items-center justify-center rounded-full bg-foreground text-background transition-transform active:scale-95">
            <Square className="size-3.5 fill-current" />
          </button>
        ) : (
          <button
            type="submit"
            disabled={!draft.trim()}
            aria-label="Ask"
            className="sheen flex size-11 shrink-0 items-center justify-center rounded-full bg-primary text-primary-foreground transition-[transform,opacity] active:scale-95 disabled:opacity-40"
          >
            <ArrowUp className="size-5" />
          </button>
        )}
      </form>
      <p className="mt-2 text-center text-xs text-muted-foreground">Enter to ask, Shift+Enter for a new line. Answers can be wrong: check the sources.</p>

      <p className="sr-only" aria-live="polite">
        {last?.status === "done" ? "Answer ready." : last?.status === "error" ? (last.error ?? "Something went wrong.") : ""}
      </p>

      <CitationDrawer citation={open} onClose={() => setOpen(null)} />
    </div>
  );
}

function Suggestions({ questions, onPick }: { questions: string[]; onPick: (question: string) => void }) {
  return (
    <div className="pt-6">
      <div className="mb-5 flex items-center gap-3">
        <span className="ai-drafted flex size-11 items-center justify-center rounded-2xl bg-violet-soft text-violet" data-generating="true">
          <Sparkles className="size-5" />
        </span>
        <p className="text-sm text-muted-foreground">Try one of these, or ask your own.</p>
      </div>
      <div className="grid gap-3 sm:grid-cols-2">
        {questions.map((question, i) => (
          <motion.button
            key={question}
            type="button"
            onClick={() => onPick(question)}
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0, transition: { delay: 0.05 * i, type: "spring", stiffness: 260, damping: 24 } }}
            whileHover={{ y: -2 }}
            whileTap={{ scale: 0.98 }}
            className="glass rounded-2xl p-4 text-left text-[0.95rem] font-medium hover:shadow-lift"
          >
            {question}
          </motion.button>
        ))}
      </div>
    </div>
  );
}

function Exchange({ turn, onCite, onRetry, canRetry }: { turn: Turn; onCite: (c: Citation) => void; onRetry: () => void; canRetry: boolean }) {
  const writing = turn.status === "writing";
  const sources = turn.sources ?? [];
  const byNumber = new Map(sources.map((s) => [s.number, s]));

  const cite = (n: number) => {
    const source = byNumber.get(n);
    // A number the model made up gets no button: there's nothing to open.
    if (!source) return <span className="text-muted-foreground">[{n}]</span>;
    return (
      <button
        type="button"
        onClick={() => onCite(source)}
        aria-label={`Source ${n}: ${source.title}`}
        className="mx-0.5 inline-flex size-[1.15rem] -translate-y-px items-center justify-center rounded-md bg-violet-soft align-middle text-[0.68rem] font-bold text-violet transition-colors hover:bg-violet hover:text-white focus-visible:ring-2 focus-visible:ring-violet focus-visible:outline-none"
      >
        {n}
      </button>
    );
  };

  return (
    <motion.section initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} className="space-y-4">
      <p className="ml-auto w-fit max-w-[85%] rounded-3xl rounded-br-lg bg-primary px-4 py-2.5 text-[0.95rem] text-primary-foreground shadow-float">{turn.question}</p>

      {turn.status === "searching" && <p className="shimmer-text text-sm font-medium">Searching manuals, policies and job history</p>}

      <AnimatePresence>
        {sources.length > 0 && (
          <motion.ol initial="hidden" animate="shown" variants={{ shown: { transition: { staggerChildren: 0.05 } } }} className="-mx-1 flex gap-2 overflow-x-auto px-1 pb-1 [scrollbar-width:none] [mask-image:linear-gradient(to_right,#000_88%,transparent)]" aria-label="Sources">
            {sources.map((source) => {
              const kind = sourceKinds[source.source];
              return (
                <motion.li key={source.number} variants={{ hidden: { opacity: 0, x: 16 }, shown: { opacity: 1, x: 0 } }} className="shrink-0">
                  <button
                    type="button"
                    onClick={() => onCite(source)}
                    className="glass flex h-full w-52 flex-col gap-1.5 rounded-2xl p-3 text-left transition-shadow hover:shadow-lift"
                  >
                    <span className="flex items-center gap-1.5 text-xs text-muted-foreground">
                      <span className="flex size-5 items-center justify-center rounded-md bg-violet-soft text-[0.68rem] font-bold text-violet">{source.number}</span>
                      <kind.icon className="size-3.5" />
                      {kind.label}
                    </span>
                    <span className="line-clamp-2 text-sm leading-snug font-medium">{source.title.split(" > ").at(-1)}</span>
                    {/* "Affected units" means nothing without the document it's in. */}
                    {source.title.includes(" > ") && <span className="truncate text-xs text-muted-foreground">{source.title.split(" > ")[0]}</span>}
                  </button>
                </motion.li>
              );
            })}
          </motion.ol>
        )}
      </AnimatePresence>

      {turn.status !== "searching" && (turn.answer || writing) && (
        <div className="ai-drafted glass-strong rounded-3xl p-5 md:p-6" data-generating={writing}>
          {turn.answer ? (
            <RichText text={turn.answer} renderCitation={cite} className={cn("text-[0.97rem]", writing && "streaming-caret")} />
          ) : (
            <p className="shimmer-text text-sm font-medium">Reading {sources.length} {sources.length === 1 ? "source" : "sources"}</p>
          )}
        </div>
      )}

      <Footer turn={turn} onRetry={onRetry} canRetry={canRetry} />
    </motion.section>
  );
}

function Footer({ turn, onRetry, canRetry }: { turn: Turn; onRetry: () => void; canRetry: boolean }) {
  const [copied, setCopied] = useState(false);

  if (turn.status === "error") {
    return (
      <p className="flex flex-wrap items-center gap-3 text-sm text-destructive">
        {turn.error}
        {canRetry && (
          <button type="button" onClick={onRetry} className="rounded-full px-2.5 py-1 font-semibold underline underline-offset-4 hover:bg-destructive/10">
            Try again
          </button>
        )}
      </p>
    );
  }

  if (turn.status === "stopped") return <p className="text-sm text-muted-foreground">Stopped.</p>;
  if (turn.status !== "done" || !turn.done) return null;

  const { done } = turn;
  async function copy() {
    await navigator.clipboard.writeText(turn.answer);
    setCopied(true);
    toast.success("Answer copied");
    setTimeout(() => setCopied(false), 1600);
  }

  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
      <span>
        {done.grounded
          ? `${done.model ?? "The model"} via ${done.provider}, ${(done.milliseconds / 1000).toFixed(1)} s`
          : "Nothing matched in the records you can see, so no model was asked."}
      </span>
      {done.grounded && (
        <button type="button" onClick={copy} className="inline-flex items-center gap-1 rounded-full px-2 py-1 font-medium hover:bg-foreground/[0.06] hover:text-foreground">
          {copied ? <Check className="size-3.5" /> : <Copy className="size-3.5" />} {copied ? "Copied" : "Copy"}
        </button>
      )}
    </div>
  );
}
