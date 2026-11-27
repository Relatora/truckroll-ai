"use client";

import { AnimatePresence, motion } from "motion/react";
import { AlertTriangle, Check, ChevronDown, Sparkles } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api";
import { useAssign, useExplanations, useSuggestions, WEIGHTS, type Explanation, type ScoreParts, type Suggestion } from "@/lib/suggestions";
import { cn } from "@/lib/utils";

/** The four parts of a score, in the order the bar draws them. Each gets one colour, everywhere. */
const PARTS: { key: keyof ScoreParts; label: string; colour: string }[] = [
  { key: "skill", label: "Skill", colour: "bg-violet" },
  { key: "fit", label: "Timing", colour: "bg-aqua" },
  { key: "distance", label: "Distance", colour: "bg-frost" },
  { key: "workload", label: "Workload", colour: "bg-ember" },
];

const time = (iso: string) => new Date(iso).toLocaleTimeString("en-CA", { hour: "numeric", minute: "2-digit" });

/**
 * "Who should go": the three best technicians for this job, ranked by the server's rules, each
 * with the facts behind the ranking and a sentence about them. Assigning is the dispatcher's
 * click, with Undo in the toast.
 */
export function SuggestionsPanel({ jobId, jobNumber }: { jobId: number; jobNumber: string }) {
  const { data, isPending, error } = useSuggestions(jobId, true);
  const explained = useExplanations(jobId, data?.top);
  const { assign, unassign } = useAssign(jobId);

  if (isPending) return <div className="glass h-72 animate-pulse rounded-3xl" aria-label="Ranking technicians" />;
  if (error || !data) return null;

  const sentences = new Map(explained.data?.explanations.map((e) => [e.technicianId, e]));

  async function onAssign(suggestion: Suggestion) {
    try {
      await assign.mutateAsync(suggestion.technicianId);
      toast.success(`Assigned ${jobNumber} to ${suggestion.name}`, {
        description: "Everyone on the dispatch board sees it now.",
        // Long enough to notice a wrong click and take it back.
        duration: 10_000,
        action: { label: "Undo", onClick: () => void unassign.mutateAsync().then(() => toast(`${jobNumber} is back in Waiting`)) },
      });
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : "Couldn't assign the job.");
    }
  }

  return (
    <section aria-labelledby="who-should-go" className="glass rounded-3xl p-5 md:p-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 id="who-should-go" className="type-title text-2xl">
            Who should go
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Ranked by licence, skill, timing, distance and workload, from {data.considered} {data.considered === 1 ? "technician" : "technicians"}.
          </p>
        </div>
        <ul className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted-foreground" aria-label="Score colours">
          {PARTS.map((p) => (
            <li key={p.key} className="flex items-center gap-1.5">
              <span className={cn("size-2 rounded-full", p.colour)} /> {p.label}
            </li>
          ))}
        </ul>
      </div>

      {data.top.length === 0 ? (
        <p className="mt-6 rounded-2xl bg-warning-soft p-4 text-sm">Nobody you can assign is free and licensed for this window. Move the window, or ask another branch to cover.</p>
      ) : (
        <ol className="mt-5 space-y-3">
          {data.top.map((suggestion, i) => (
            <Row
              key={suggestion.technicianId}
              suggestion={suggestion}
              index={i}
              explanation={sentences.get(suggestion.technicianId)}
              explaining={explained.isPending}
              current={data.currentTechnicianId === suggestion.technicianId}
              canAssign={data.canAssign}
              busy={assign.isPending}
              onAssign={() => onAssign(suggestion)}
            />
          ))}
        </ol>
      )}

      {data.ruledOut.length > 0 && (
        <details className="group mt-4 text-sm">
          <summary className="inline-flex cursor-pointer list-none items-center gap-1 rounded-full px-2 py-1 font-medium text-muted-foreground hover:bg-foreground/[0.05] hover:text-foreground">
            <ChevronDown className="size-4 transition-transform group-open:rotate-180" /> Why not the others ({data.ruledOut.length})
          </summary>
          <ul className="mt-2 grid gap-1.5 pl-2 sm:grid-cols-2">
            {data.ruledOut.map((r) => (
              <li key={r.technicianId} className="text-muted-foreground">
                <span className="font-medium text-foreground">{r.name}</span>: {r.reason}
              </li>
            ))}
          </ul>
        </details>
      )}

      {explained.data?.model && explained.data.explanations.some((e) => e.writtenBy === "model") && (
        <p className="mt-4 text-xs text-muted-foreground">Sentences by {explained.data.model}. The order and every number come from the rules, never the model.</p>
      )}
    </section>
  );
}

function Row({
  suggestion: s,
  index,
  explanation,
  explaining,
  current,
  canAssign,
  busy,
  onAssign,
}: {
  suggestion: Suggestion;
  index: number;
  explanation?: Explanation;
  explaining: boolean;
  current: boolean;
  canAssign: boolean;
  busy: boolean;
  onAssign: () => void;
}) {
  const f = s.facts;
  const breakdown = PARTS.map((p) => `${p.label.toLowerCase()} ${(WEIGHTS[p.key] * s.parts[p.key]).toFixed(2)}`).join(", ");
  const byModel = explanation?.writtenBy === "model";

  return (
    <motion.li
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0, transition: { delay: index * 0.07, type: "spring", stiffness: 260, damping: 26 } }}
      className={cn("rounded-2xl border border-foreground/[0.07] bg-background/40 p-4", index === 0 && "border-violet/30 shadow-float")}
    >
      <div className="flex flex-wrap items-center gap-x-4 gap-y-3">
        <span className={cn("flex size-8 shrink-0 items-center justify-center rounded-xl text-sm font-bold", index === 0 ? "bg-violet text-white" : "bg-foreground/[0.07]")}>{s.rank}</span>
        <div className="min-w-36 flex-1">
          <p className="flex items-center gap-2 font-semibold">
            <span className="size-2.5 rounded-full" style={{ background: s.color }} />
            {s.name}
          </p>
          {/* The score, split into what earned it. Width is the score out of a perfect 1. */}
          <div className="mt-2 flex h-2 w-full max-w-64 overflow-hidden rounded-full bg-foreground/[0.07]" role="img" aria-label={`Score ${s.score.toFixed(2)} of 1: ${breakdown}`}>
            {PARTS.map((p) => (
              <motion.span
                key={p.key}
                className={cn("h-full", p.colour)}
                initial={{ width: 0 }}
                animate={{ width: `${WEIGHTS[p.key] * s.parts[p.key] * 100}%` }}
                transition={{ delay: 0.15 + index * 0.07, type: "spring", stiffness: 120, damping: 22 }}
              />
            ))}
          </div>
        </div>
        {current ? (
          <span className="inline-flex h-9 items-center gap-1.5 rounded-full bg-success-soft px-3.5 text-sm font-semibold text-success">
            <Check className="size-4" /> Assigned
          </span>
        ) : (
          canAssign && (
            <Button size="lg" variant={index === 0 ? "default" : "ghost"} className={cn("rounded-full px-4", index === 0 ? "sheen" : "glass")} disabled={busy} onClick={onAssign}>
              Assign {s.name.split(" ")[0]}
            </Button>
          )
        )}
      </div>

      <ul className="mt-3 flex flex-wrap gap-1.5 text-xs">
        <Chip>{f.kilometres} km from {f.comingFrom}</Chip>
        <Chip>{f.minutesLate === 0 ? `Free by ${time(f.freeFrom)}, on time` : `Arrives ${time(f.arrives)}, ${f.minutesLate} min late`}</Chip>
        <Chip>{f.jobsThatDay === 0 ? "Nothing else that day" : `${f.jobsThatDay} other ${f.jobsThatDay === 1 ? "job" : "jobs"} that day`}</Chip>
        {!f.hasSkill && <Chip warn>Not their usual work</Chip>}
        {f.makesLateFor && (
          <Chip warn>
            <AlertTriangle className="size-3" /> Makes {f.makesLateFor} late
          </Chip>
        )}
      </ul>

      <div className={cn("mt-3 rounded-xl px-3 py-2 text-sm", byModel && "ai-drafted bg-violet-soft/40")} data-generating={explaining}>
        <AnimatePresence mode="wait" initial={false}>
          <motion.p
            key={explanation?.sentence ?? s.sentence}
            initial={{ opacity: 0, filter: "blur(4px)" }}
            animate={{ opacity: 1, filter: "blur(0px)" }}
            exit={{ opacity: 0 }}
            className={cn("flex gap-2", !byModel && "text-muted-foreground", explaining && "shimmer-text")}
          >
            {byModel && <Sparkles className="mt-0.5 size-3.5 shrink-0 text-violet" aria-label="Written by the model" />}
            {explanation?.sentence ?? s.sentence}
          </motion.p>
        </AnimatePresence>
      </div>
    </motion.li>
  );
}

function Chip({ children, warn }: { children: React.ReactNode; warn?: boolean }) {
  return (
    <li className={cn("inline-flex items-center gap-1 rounded-full px-2.5 py-1", warn ? "bg-warning-soft font-medium text-foreground [&_svg]:text-warning" : "bg-foreground/[0.05] text-muted-foreground")}>
      {children}
    </li>
  );
}
