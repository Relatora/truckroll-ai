"use client";

import { AnimatePresence, motion } from "motion/react";
import { Ban, Check, Clock3, Phone, Sparkles } from "lucide-react";
import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";

/**
 * Living demos of the three AI features, played with the demo company's own stories. Each starts
 * when its chapter scrolls into view (`active`) and settles into a still frame afterwards. Under
 * reduced motion they show the finished frame straight away.
 */

function useReducedMotionPreference() {
  const [reduced, setReduced] = useState(false);
  useEffect(() => {
    const query = window.matchMedia("(prefers-reduced-motion: reduce)");
    const update = () => setReduced(query.matches);
    update();
    query.addEventListener("change", update);
    return () => query.removeEventListener("change", update);
  }, []);
  return reduced;
}

/** Steps through a timeline of delays once `active` turns true. Returns the current step. */
function useTimeline(active: boolean, delays: number[]) {
  const reduced = useReducedMotionPreference();
  const [step, setStep] = useState(0);
  const key = delays.join(",");

  useEffect(() => {
    if (!active || reduced) {
      return;
    }
    let elapsed = 0;
    const timers = key.split(",").map((delay, index) => {
      elapsed += Number(delay);
      return setTimeout(() => setStep(index + 1), elapsed);
    });
    return () => timers.forEach(clearTimeout);
  }, [active, reduced, key]);

  return reduced ? delays.length : step;
}

// ------------------------------------------------------------------ 1. Ask Truckroll

const ANSWER: (string | number)[] = [
  "A", "Calderon", "CAL-80-100", "furnace", "and", "a", "Frostline", "FL-16", "air", "conditioner,", "both", "installed", "in", "October", "2020.", 1,
  "The", "furnace's", "hot", "surface", "ignitor", "has", "cracked", "twice,", "most", "recently", "last", "month.", 2,
  "Its", "serial", "number", "falls", "in", "the", "range", "of", "bulletin", "SB-2024-07,", "which", "covers", "the", "ignitor,", "a", "revised", "bracket", "and", "an", "hour", "of", "labour.", 3,
];

export function AnswerDemo({ active }: { active: boolean }) {
  const reduced = useReducedMotionPreference();
  const [count, setCount] = useState(0);

  useEffect(() => {
    if (!active || reduced) {
      return;
    }
    let shown = 0;
    const timer = setInterval(() => {
      shown += 1;
      setCount(shown);
      if (shown >= ANSWER.length) {
        clearInterval(timer);
      }
    }, 45);
    return () => clearInterval(timer);
  }, [active, reduced]);

  const visible = reduced ? ANSWER.length : count;
  const done = visible >= ANSWER.length;

  return (
    <div className="space-y-4 text-sm">
      <div className="ml-auto w-fit max-w-[85%] rounded-2xl rounded-br-md bg-primary px-4 py-2.5 text-primary-foreground">
        What did we install at 14 Maple St, and has it given trouble?
      </div>
      <div data-generating={!done && visible > 0} className="ai-drafted glass rounded-2xl rounded-bl-md p-4 leading-relaxed">
        <p className="mb-2 flex items-center gap-1.5 text-xs font-semibold text-violet">
          <Sparkles className="size-3.5" aria-hidden /> Ask Truckroll
        </p>
        <p className="min-h-[7.5rem]">
          {ANSWER.slice(0, visible).map((part, index) =>
            typeof part === "number" ? (
              <motion.sup
                key={index}
                initial={{ scale: 0 }}
                animate={{ scale: 1 }}
                transition={{ type: "spring", stiffness: 600, damping: 15 }}
                className="mx-0.5 inline-flex size-[1.15rem] items-center justify-center rounded-md bg-frost-soft text-[0.65rem] font-bold text-frost"
              >
                {part}
              </motion.sup>
            ) : (
              <span key={index}>{part} </span>
            ),
          )}
          {!done && <span className="ml-0.5 inline-block h-4 w-1.5 translate-y-0.5 animate-pulse rounded-sm bg-violet" />}
        </p>
        <AnimatePresence>
          {done && (
            <motion.ol initial={{ opacity: 0, y: 6 }} animate={{ opacity: 1, y: 0 }} className="mt-3 space-y-1.5 border-t pt-3 text-xs text-muted-foreground">
              {["Equipment at 14 Maple St, Guelph", "Job note by Nadia Kowalczyk", "SB-2024-07, CAL-80 ignitor cracking"].map((source, index) => (
                <motion.li key={source} initial={{ opacity: 0, x: -6 }} animate={{ opacity: 1, x: 0 }} transition={{ delay: index * 0.08 }} className="flex items-center gap-2">
                  <span className="inline-flex size-4 items-center justify-center rounded bg-frost-soft text-[0.6rem] font-bold text-frost">{index + 1}</span>
                  {source}
                </motion.li>
              ))}
            </motion.ol>
          )}
        </AnimatePresence>
      </div>
    </div>
  );
}

// ------------------------------------------------------------------ 2. Dispatch suggestions

type Candidate = { name: string; distance: string; licence: string; eligible: boolean; reason: string; score: number };

const CANDIDATES: Candidate[] = [
  { name: "Tyler Fischer", distance: "1.2 km", licence: "G3", eligible: false, reason: "A G3 can't attend a gas emergency alone", score: 0 },
  { name: "Ben Okoro", distance: "2.0 km", licence: "313A", eligible: false, reason: "No gas licence", score: 0 },
  { name: "Nadia Kowalczyk", distance: "3.1 km", licence: "G2", eligible: true, reason: "Furnace specialist, free from noon", score: 92 },
  { name: "Marco Bellini", distance: "28 km", licence: "G1", eligible: true, reason: "Licensed, but in Kitchener", score: 41 },
];

export function DispatchDemo({ active }: { active: boolean }) {
  // 1: licences checked, 2: ranked, 3: explained
  const step = useTimeline(active, [700, 1000, 900]);
  const ordered = step >= 2 ? [...CANDIDATES].sort((a, b) => b.score - a.score) : CANDIDATES;

  return (
    <div className="space-y-3 text-sm">
      <div className="glass flex items-center justify-between gap-3 rounded-2xl p-4">
        <div>
          <p className="font-semibold">No heat, 14 Maple St</p>
          <p className="text-muted-foreground">CAL-80 furnace, needs a G2 or G1</p>
        </div>
        <span className="rounded-full bg-ember px-2.5 py-1 text-xs font-bold text-white glow-ember">Emergency</span>
      </div>

      <ul className="space-y-2">
        {ordered.map((candidate, index) => {
          const ruledOut = step >= 1 && !candidate.eligible;
          return (
            <motion.li
              key={candidate.name}
              layout
              transition={{ type: "spring", stiffness: 380, damping: 30 }}
              className={cn(
                "glass flex items-center gap-3 rounded-xl px-3 py-2.5 transition-opacity duration-500",
                ruledOut && "opacity-55",
                step >= 2 && index === 0 && "ring-2 ring-success/60",
              )}
            >
              <span className={cn("flex size-8 shrink-0 items-center justify-center rounded-lg text-xs font-bold", ruledOut ? "bg-destructive/10 text-destructive" : "bg-foreground/[0.06]")}>
                {ruledOut ? <Ban className="size-4" /> : candidate.licence}
              </span>
              <span className="min-w-0 flex-1">
                <span className={cn("block font-medium", ruledOut && "line-through decoration-destructive/60")}>{candidate.name}</span>
                <span className="block truncate text-xs text-muted-foreground">
                  {step >= 1 ? candidate.reason : `${candidate.distance} away`}
                </span>
              </span>
              {step >= 2 && candidate.eligible && (
                <span className="w-16">
                  <span className="block h-1.5 overflow-hidden rounded-full bg-foreground/[0.07]">
                    <motion.span
                      initial={{ width: 0 }}
                      animate={{ width: `${candidate.score}%` }}
                      transition={{ type: "spring", stiffness: 120, damping: 20 }}
                      className={cn("block h-full rounded-full", index === 0 ? "bg-success" : "bg-warning")}
                    />
                  </span>
                </span>
              )}
            </motion.li>
          );
        })}
      </ul>

      <AnimatePresence>
        {step >= 3 && (
          <motion.div initial={{ opacity: 0, y: 8, scale: 0.98 }} animate={{ opacity: 1, y: 0, scale: 1 }} className="ai-drafted glass flex items-center justify-between gap-3 rounded-2xl p-3.5">
            <p className="text-muted-foreground">
              <span className="font-semibold text-foreground">Send Nadia.</span> She holds the G2 and finishes 3 km away at 11:30.
            </p>
            <span className="shrink-0 rounded-lg bg-primary px-3 py-1.5 text-xs font-semibold text-primary-foreground">Assign</span>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

// ------------------------------------------------------------------ 3. Calls to tickets

const TRANSCRIPT = [
  ["Doug", "The pressure's dropping again. The gauge was down near half this morning."],
  ["Sam", "Is the heat still working?"],
  ["Doug", "It's working. There's a bit of water at the bottom of that pipe on the side."],
  ["Doug", "Friday morning's fine. Ring the bell twice."],
];

export function CallDemo({ active }: { active: boolean }) {
  // 1-4: lines, 5: summarising, 6: proposal
  const step = useTimeline(active, [300, 600, 600, 600, 700, 1300]);

  return (
    <div className="space-y-3 text-sm">
      <p className="flex items-center gap-2 text-muted-foreground">
        <Phone className="size-3.5" aria-hidden /> Doug Hartley, Kitchener, 8:05 am
      </p>
      <motion.div
        animate={{ height: step >= 6 ? 64 : "auto", opacity: step >= 6 ? 0.55 : 1 }}
        transition={{ type: "spring", stiffness: 200, damping: 28 }}
        className="glass space-y-2 overflow-hidden rounded-2xl p-4"
      >
        {TRANSCRIPT.map(([who, line], index) => (
          <motion.p key={index} initial={false} animate={{ opacity: step > index ? 1 : 0, y: step > index ? 0 : 6 }}>
            <span className="font-semibold">{who}:</span> <span className="text-muted-foreground">{line}</span>
          </motion.p>
        ))}
      </motion.div>

      <AnimatePresence mode="wait">
        {step === 5 && (
          <motion.div key="thinking" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} data-generating className="ai-drafted glass flex items-center gap-2 rounded-2xl p-4 text-muted-foreground">
            <Sparkles className="size-4 animate-pulse text-violet" aria-hidden /> Reading the call…
          </motion.div>
        )}
        {step >= 6 && (
          <motion.div key="proposal" initial={{ opacity: 0, y: 10, scale: 0.98 }} animate={{ opacity: 1, y: 0, scale: 1 }} className="ai-drafted glass space-y-3 rounded-2xl p-4">
            <p>
              <span className="font-semibold">Summary.</span> Boiler pressure keeps dropping; the customer refills it every other day.
              Water at the relief valve outlet.
            </p>
            <p className="flex items-center gap-2">
              <Clock3 className="size-3.5 text-muted-foreground" aria-hidden /> Repair, Hearthmark HMB-110, Friday 8 to 10 am
            </p>
            <p className="text-muted-foreground">Follow-up drafted: &ldquo;Your boiler visit on Friday morning&rdquo;</p>
            <div className="flex items-center justify-between gap-2 border-t pt-3">
              <span className="flex items-center gap-1.5 text-xs text-muted-foreground">
                <span className="size-1.5 animate-pulse rounded-full bg-warning" /> Waiting for Sam to approve
              </span>
              <span className="flex items-center gap-1 rounded-lg bg-primary px-3 py-1.5 text-xs font-semibold text-primary-foreground">
                <Check className="size-3.5" /> Approve
              </span>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
