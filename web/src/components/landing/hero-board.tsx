"use client";

import { LayoutGroup, motion, useReducedMotion } from "motion/react";
import { RotateCcw } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";

/**
 * The landing page's one animated moment: a miniature of Guelph's dispatch board.
 *
 * It plays once. The day's jobs settle into the technicians' lanes, an emergency appears with no
 * one on it, a suggestion explains who can legally take it, and the job moves into Nadia's lane.
 * That's the product in eight seconds. With reduced motion it shows the finished board.
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
  { id: "c", lane: "nadia", start: 14.5, end: 16, label: "Boiler pressure check", kind: "heat" },
  { id: "d", lane: "ben", start: 8, end: 10, label: "Heat pump iced over", kind: "cool" },
  { id: "e", lane: "ben", start: 11, end: 13, label: "Heat pump estimate", kind: "cool" },
  { id: "f", lane: "ben", start: 14, end: 15.5, label: "AC winterising", kind: "cool" },
  { id: "g", lane: "tyler", start: 9, end: 10.5, label: "Humidifier panel", kind: "air" },
  { id: "h", lane: "tyler", start: 12.5, end: 14, label: "Air exchanger noise", kind: "air" },
];

const KIND_STYLES: Record<Kind, string> = {
  heat: "bg-ember/20 text-orange-50 ring-ember/50",
  cool: "bg-frost/25 text-sky-50 ring-frost/50",
  water: "bg-aqua/25 text-teal-50 ring-aqua/50",
  air: "bg-white/10 text-slate-100 ring-white/20",
};

const at = (hour: number) => `${((hour - DAY_START) / DAY_HOURS) * 100}%`;
const span = (start: number, end: number) => `${((end - start) / DAY_HOURS) * 100}%`;

export function HeroBoard() {
  const reduced = useReducedMotion();
  const [step, setStep] = useState(0);
  const [run, setRun] = useState(0);
  const scroller = useRef<HTMLDivElement>(null);

  // On a phone the board is wider than the screen. Start it scrolled to late morning, where the
  // emergency lands, instead of at 8 am with the story off-screen.
  useEffect(() => {
    const element = scroller.current;
    if (element && element.scrollWidth > element.clientWidth) {
      element.scrollLeft = element.scrollWidth * 0.42 - element.clientWidth * 0.35;
    }
  }, []);

  // 0: empty · 1: the day appears · 2: the suggestion · 3: assigned
  useEffect(() => {
    if (reduced) {
      return;
    }
    const timers = [
      setTimeout(() => setStep(1), 250),
      setTimeout(() => setStep(2), 2200),
      setTimeout(() => setStep(3), 5200),
    ];
    return () => timers.forEach(clearTimeout);
  }, [reduced, run]);

  const shown = reduced ? 3 : step;
  const emergency = (
    <motion.div
      layoutId="emergency"
      transition={{ type: "spring", stiffness: 140, damping: 20 }}
      className="absolute inset-y-1.5 flex items-center overflow-hidden rounded-md bg-ember px-2.5 text-[0.8rem] font-semibold text-white shadow-[0_6px_24px_-6px] shadow-ember/70"
      style={{ left: at(12), width: span(12, 14) }}
    >
      <span className="truncate">No heat, 14 Maple St</span>
    </motion.div>
  );

  return (
    <figure className="relative">
      <div ref={scroller} className="overflow-x-auto rounded-2xl bg-white/[0.04] ring-1 ring-white/10">
        <div className="min-w-[44rem] p-4 md:p-5">
          <div className="mb-3 flex items-baseline justify-between gap-4 text-sm">
            <p className="font-semibold text-white">Guelph, this morning</p>
            <p className="text-slate-300">3 technicians, 9 jobs</p>
          </div>

          <LayoutGroup>
            <div className="grid grid-cols-[9.5rem_1fr] gap-y-1.5">
              <div />
              <div className="relative h-6 text-xs text-slate-400">
                {[8, 10, 12, 14, 16].map((hour) => (
                  <span
                    key={hour}
                    // Centred on its hour, except the first and last, which sit inside the track.
                    className={cn("absolute tabular whitespace-nowrap", hour === 8 ? "" : hour === 16 ? "-translate-x-full" : "-translate-x-1/2")}
                    style={{ left: at(hour) }}
                  >
                    {hour === 12 ? "noon" : `${hour > 12 ? hour - 12 : hour}${hour < 12 ? " am" : " pm"}`}
                  </span>
                ))}
              </div>

              {LANES.map((lane) => (
                <Lane key={lane.id} label={lane.name} detail={lane.licence} nowVisible={shown >= 1} reduced={!!reduced}>
                  {BLOCKS.filter((b) => b.lane === lane.id).map((block, index) => (
                    <motion.div
                      key={`${block.id}-${run}`}
                      initial={reduced ? false : { opacity: 0, y: 6 }}
                      animate={shown >= 1 ? { opacity: 1, y: 0 } : { opacity: 0, y: 6 }}
                      transition={{ delay: reduced ? 0 : 0.12 * index + LANES.findIndex((l) => l.id === lane.id) * 0.18, duration: 0.45 }}
                      className={cn(
                        "absolute inset-y-1.5 flex items-center overflow-hidden rounded-md px-2.5 text-[0.8rem] font-medium ring-1 ring-inset",
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

              <Lane label="Not assigned" detail="Needs a G2 or G1" muted nowVisible={shown >= 1} reduced={!!reduced}>
                {shown >= 1 && shown < 3 && (
                  <motion.div
                    initial={reduced ? false : { opacity: 0, scale: 0.96 }}
                    animate={{ opacity: 1, scale: 1 }}
                    transition={{ delay: reduced ? 0 : 1.2, duration: 0.4 }}
                    className="absolute inset-0"
                  >
                    {emergency}
                  </motion.div>
                )}
              </Lane>
            </div>
          </LayoutGroup>

        </div>
      </div>

      <figcaption className="mt-5 flex flex-col gap-4 text-sm text-slate-300 md:flex-row md:items-start md:justify-between">
        <div className="max-w-sm space-y-2">
          <p>From the demo data: an emergency that needs a gas licence, and who can legally take it.</p>
          {!reduced && shown >= 3 && (
            <button
              type="button"
              onClick={() => {
                setStep(0);
                setRun((value) => value + 1);
              }}
              className="-ml-2 inline-flex items-center gap-1.5 rounded-md px-2 py-1 font-medium text-white hover:bg-white/10 focus-visible:ring-2 focus-visible:ring-frost focus-visible:outline-none"
            >
              <RotateCcw className="size-3.5" aria-hidden />
              Play again
            </button>
          )}
        </div>
        <Suggestion visible={shown === 2} assigned={shown >= 3} reduced={!!reduced} />
      </figcaption>
    </figure>
  );
}

function Lane({
  label,
  detail,
  muted = false,
  nowVisible,
  reduced,
  children,
}: {
  label: string;
  detail: string;
  muted?: boolean;
  nowVisible: boolean;
  reduced: boolean;
  children: React.ReactNode;
}) {
  return (
    <>
      {/* Sticky, so the names stay put when a phone scrolls the board sideways. The solid colour
          matches the board's translucent panel over the navy band. */}
      <div className="sticky left-0 z-20 -ml-4 flex flex-col justify-center bg-[#1d2a45] pr-3 pl-4 md:static md:ml-0 md:bg-transparent md:pl-0 dark:bg-[#141f33] md:dark:bg-transparent">
        <span className={cn("truncate text-sm font-medium", muted ? "text-slate-300" : "text-white")}>{label}</span>
        <span className="truncate text-xs text-slate-400">{detail}</span>
      </div>
      <div className={cn("relative h-12 rounded-lg", muted ? "border border-dashed border-white/15" : "bg-white/[0.05]")}>
        {children}
        {/* The current time, drawn through each lane. */}
        <motion.div
          aria-hidden
          initial={reduced ? false : { scaleY: 0 }}
          animate={{ scaleY: nowVisible ? 1 : 0 }}
          transition={{ duration: 0.6, delay: reduced ? 0 : 0.5 }}
          className="pointer-events-none absolute -inset-y-1 z-10 w-px origin-top bg-white/70"
          style={{ left: at(NOW) }}
        />
      </div>
    </>
  );
}

/**
 * The AI's part, in the thermal frame that marks model-written text. The ranking itself is plain
 * code (licences first, then skill, distance and the day's load); the model only writes the reason.
 */
function Suggestion({ visible, assigned, reduced }: { visible: boolean; assigned: boolean; reduced: boolean }) {
  return (
    <motion.div
      initial={false}
      animate={{ opacity: visible || assigned ? 1 : 0, y: visible || assigned ? 0 : 8 }}
      transition={{ duration: reduced ? 0 : 0.45 }}
      aria-live="polite"
      className="ai-drafted min-h-[7.5rem] rounded-xl bg-[#101c33] p-4 text-sm md:w-[24rem] md:shrink-0"
    >
      {assigned ? (
        <p className="text-slate-100">
          <span className="font-semibold text-white">Assigned to Nadia.</span> She&apos;s free from noon and finishes 3 km away.
        </p>
      ) : (
        <>
          <p className="font-semibold text-white">Suggested: Nadia Kowalczyk</p>
          <p className="mt-1 text-slate-300">
            Holds the G2 a gas emergency needs and finishes her 10 am job 3 km away. Tyler is closer but holds a G3,
            and Ben has no gas licence.
          </p>
        </>
      )}
    </motion.div>
  );
}
