"use client";

import { LayoutGroup, motion, useReducedMotion } from "motion/react";
import { RotateCcw, Sparkles } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";

/**
 * A miniature of Guelph's dispatch board that plays the product's story once.
 *
 * The day's jobs settle into the technicians' lanes, an emergency arrives with no one on it, a
 * suggestion explains who can legally take it, and the job glides into Nadia's lane. With reduced
 * motion it shows the finished board. On a phone it opens scrolled to late morning.
 */

type Kind = "heat" | "cool" | "water" | "air";
type Block = { id: string; lane: string; start: number; end: number; label: string; kind: Kind };

const DAY_START = 8;
const DAY_HOURS = 8;
const NOW = 11.25;

const LANES = [
  { id: "nadia", name: "Nadia Kowalczyk", licence: "G2 gas" },
  { id: "ben", name: "Ben Okoro", licence: "313A refrigeration" },
  { id: "tyler", name: "Tyler Fischer", licence: "G3 gas" },
];

const BLOCKS: Block[] = [
  { id: "a", lane: "nadia", start: 8, end: 9.5, label: "Furnace tune-up", kind: "heat" },
  { id: "b", lane: "nadia", start: 10, end: 11.5, label: "No hot water, AV-50", kind: "water" },
  { id: "c", lane: "nadia", start: 14.5, end: 16, label: "Boiler pressure", kind: "heat" },
  { id: "d", lane: "ben", start: 8, end: 10, label: "Heat pump iced over", kind: "cool" },
  { id: "e", lane: "ben", start: 11, end: 13, label: "Heat pump estimate", kind: "cool" },
  { id: "f", lane: "ben", start: 14, end: 15.5, label: "AC winterising", kind: "cool" },
  { id: "g", lane: "tyler", start: 9, end: 10.5, label: "Humidifier panel", kind: "air" },
  { id: "h", lane: "tyler", start: 12.5, end: 14, label: "Air exchanger noise", kind: "air" },
];

const KIND_STYLES: Record<Kind, string> = {
  heat: "bg-ember-soft before:bg-ember",
  cool: "bg-frost-soft before:bg-frost",
  water: "bg-aqua-soft before:bg-aqua",
  air: "bg-violet-soft before:bg-violet",
};

const at = (hour: number) => `${((hour - DAY_START) / DAY_HOURS) * 100}%`;
const span = (start: number, end: number) => `${((end - start) / DAY_HOURS) * 100}%`;

export function HeroBoard() {
  const reduced = useReducedMotion();
  const [step, setStep] = useState(0);
  const [run, setRun] = useState(0);
  const scroller = useRef<HTMLDivElement>(null);

  // On a phone the board is wider than the screen: start at late morning, where the story is.
  useEffect(() => {
    const element = scroller.current;
    if (element && element.scrollWidth > element.clientWidth) {
      element.scrollLeft = element.scrollWidth * 0.42 - element.clientWidth * 0.35;
    }
  }, []);

  // 0 empty, 1 the day appears, 2 the suggestion, 3 assigned.
  useEffect(() => {
    if (reduced) {
      return;
    }
    const timers = [setTimeout(() => setStep(1), 500), setTimeout(() => setStep(2), 2600), setTimeout(() => setStep(3), 5600)];
    return () => timers.forEach(clearTimeout);
  }, [reduced, run]);

  const shown = reduced ? 3 : step;

  const emergency = (
    <motion.div
      layoutId="emergency"
      transition={{ type: "spring", stiffness: 130, damping: 18 }}
      className="absolute inset-y-1.5 z-10 flex items-center rounded-lg bg-ember px-2.5 text-[0.8rem] font-semibold text-white glow-ember"
      style={{ left: at(12), width: span(12, 14) }}
    >
      {shown < 3 && (
        <span aria-hidden className="absolute -inset-1 rounded-xl border-2 border-ember" style={{ animation: "pulse-ring 1.4s ease-out infinite" }} />
      )}
      <span className="truncate">No heat, 14 Maple St</span>
    </motion.div>
  );

  return (
    <figure>
      <div className="glass-strong overflow-hidden rounded-3xl">
        <div ref={scroller} className="overflow-x-auto">
          <div className="min-w-[44rem] p-4 md:p-6">
            <div className="mb-4 flex items-center justify-between gap-4">
              <p className="flex items-center gap-2 font-display text-lg font-semibold">
                <span className="relative flex size-2.5">
                  <span className="absolute inset-0 rounded-full bg-success" style={{ animation: "pulse-ring 1.8s ease-out infinite" }} />
                  <span className="relative size-2.5 rounded-full bg-success" />
                </span>
                Guelph, this morning
              </p>
              <p className="text-sm text-muted-foreground">3 technicians, 9 jobs</p>
            </div>

            <LayoutGroup id={`board-${run}`}>
              <div className="grid grid-cols-[9.5rem_1fr] gap-y-2">
                <div />
                <div className="relative h-6 text-xs font-medium text-muted-foreground">
                  {[8, 10, 12, 14, 16].map((hour) => (
                    <span
                      key={hour}
                      className={cn("absolute tabular whitespace-nowrap", hour === 8 ? "" : hour === 16 ? "-translate-x-full" : "-translate-x-1/2")}
                      style={{ left: at(hour) }}
                    >
                      {hour === 12 ? "noon" : `${hour > 12 ? hour - 12 : hour}${hour < 12 ? " am" : " pm"}`}
                    </span>
                  ))}
                </div>

                {LANES.map((lane, laneIndex) => (
                  <Lane key={lane.id} label={lane.name} detail={lane.licence} nowVisible={shown >= 1}>
                    {BLOCKS.filter((b) => b.lane === lane.id).map((block, index) => (
                      <motion.div
                        key={`${block.id}-${run}`}
                        initial={{ opacity: 0, scale: 0.85, y: 8 }}
                        animate={shown >= 1 ? { opacity: 1, scale: 1, y: 0 } : { opacity: 0, scale: 0.85, y: 8 }}
                        transition={{ type: "spring", stiffness: 380, damping: 24, delay: 0.1 * index + laneIndex * 0.15 }}
                        whileHover={{ y: -2, scale: 1.03 }}
                        className={cn(
                          "absolute inset-y-1.5 flex items-center overflow-hidden rounded-lg pr-2 pl-3.5 text-[0.8rem] font-medium text-foreground before:absolute before:inset-y-1.5 before:left-1 before:w-1 before:rounded-full",
                          KIND_STYLES[block.kind],
                        )}
                        style={{ left: at(block.start), width: span(block.start, block.end) }}
                      >
                        <span className="truncate">{block.label}</span>
                      </motion.div>
                    ))}
                    {lane.id === "nadia" && shown >= 3 && emergency}
                  </Lane>
                ))}

                <Lane label="Not assigned" detail="Needs a G2 or G1" muted nowVisible={shown >= 1}>
                  {shown >= 1 && shown < 3 && (
                    <motion.div initial={{ opacity: 0, scale: 0.7 }} animate={{ opacity: 1, scale: 1 }} transition={{ delay: 1.3, type: "spring", stiffness: 300, damping: 16 }} className="absolute inset-0">
                      {emergency}
                    </motion.div>
                  )}
                </Lane>
              </div>
            </LayoutGroup>
          </div>
        </div>
      </div>

      <figcaption className="mt-5 flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="max-w-sm space-y-2 text-sm text-muted-foreground">
          <p>From the demo data: an emergency that needs a gas licence, and who can legally take it.</p>
          {!reduced && shown >= 3 && (
            <button
              type="button"
              onClick={() => {
                setStep(0);
                setRun((value) => value + 1);
              }}
              className="-ml-2 inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 font-medium text-foreground transition-colors hover:bg-foreground/[0.06] focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
            >
              <RotateCcw className="size-3.5" aria-hidden />
              Play again
            </button>
          )}
        </div>
        <Suggestion step={shown} />
      </figcaption>
    </figure>
  );
}

function Lane({ label, detail, muted = false, nowVisible, children }: { label: string; detail: string; muted?: boolean; nowVisible: boolean; children: React.ReactNode }) {
  return (
    <>
      {/* Sticky on a phone, so the names stay put when the board scrolls sideways. */}
      <div className="sticky left-0 z-20 -ml-4 flex flex-col justify-center bg-card pr-3 pl-4 md:static md:ml-0 md:bg-transparent md:pl-0">
        <span className={cn("truncate text-sm font-semibold", muted && "text-muted-foreground")}>{label}</span>
        <span className="truncate text-xs text-muted-foreground">{detail}</span>
      </div>
      <div className={cn("relative h-12 rounded-xl", muted ? "border border-dashed border-foreground/15" : "bg-foreground/[0.035]")}>
        {children}
        <motion.div
          aria-hidden
          initial={{ scaleY: 0 }}
          animate={{ scaleY: nowVisible ? 1 : 0 }}
          transition={{ duration: 0.6, delay: 0.5, ease: [0.16, 1, 0.3, 1] }}
          className="pointer-events-none absolute -inset-y-1 z-20 w-0.5 origin-top rounded-full bg-foreground/60"
          style={{ left: at(NOW) }}
        />
      </div>
    </>
  );
}

/**
 * The AI's part, in the thermal frame that marks model-written text. The frame turns while the
 * suggestion is "thinking". The ranking is plain code; the model only writes the reason.
 */
function Suggestion({ step }: { step: number }) {
  const visible = step >= 2;
  return (
    <motion.div
      initial={false}
      animate={{ opacity: visible ? 1 : 0, y: visible ? 0 : 10, scale: visible ? 1 : 0.98 }}
      aria-live="polite"
      data-generating={step === 2}
      className="ai-drafted glass min-h-[7.5rem] rounded-2xl p-4 text-sm md:w-[25rem] md:shrink-0"
    >
      <p className="flex items-center gap-2 font-semibold">
        <Sparkles className="size-4 text-violet" aria-hidden />
        {step >= 3 ? "Assigned to Nadia" : "Suggested: Nadia Kowalczyk"}
      </p>
      <p className="mt-1.5 text-muted-foreground">
        {step >= 3
          ? "She's free from noon, finishes 3 km away, and holds the G2 a gas emergency needs."
          : "Holds the G2 a gas emergency needs and finishes her 10 am job 3 km away. Tyler is closer but holds a G3, and Ben has no gas licence."}
      </p>
    </motion.div>
  );
}
