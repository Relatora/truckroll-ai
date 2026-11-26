"use client";

import { AnimatePresence, motion, useInView } from "motion/react";
import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";
import { AnswerDemo, CallDemo, DispatchDemo } from "./demos";

const CHAPTERS = [
  {
    title: "Ask what's in the basement",
    body: "Staff ask in plain words and get an answer built from your own equipment records, job notes and manuals. Every claim links to where it came from, so nobody has to take the model's word for it.",
    Demo: AnswerDemo,
  },
  {
    title: "Send the technician who's allowed to do the job",
    body: "Licences come first, then skills, distance and the rest of the day, all in plain code you can audit. The model only explains the pick, and the dispatcher makes the call.",
    Demo: DispatchDemo,
  },
  {
    title: "Turn a phone call into a ticket",
    body: "Paste a transcript and get a summary, a proposed job and a follow-up email. Nothing is booked or sent until someone on your team approves it.",
    Demo: CallDemo,
  },
];

/**
 * The three AI features told as a scroll story. On a wide screen the chapters scroll past on
 * the left while one glass "device" on the right stays put and switches to each chapter's live
 * demo. On a phone each chapter simply carries its own demo.
 */
export function FeatureStory() {
  const [active, setActive] = useState(0);
  const { Demo } = CHAPTERS[active];

  return (
    <div className="grid gap-16 md:grid-cols-2 md:gap-20">
      <div>
        {CHAPTERS.map((chapter, index) => (
          <Chapter key={chapter.title} index={index} active={active === index} onEnter={() => setActive(index)} {...chapter} />
        ))}
      </div>

      <div className="hidden md:block">
        <div className="sticky top-28">
          <div className="glass-strong rounded-3xl p-5">
            <div className="mb-4 flex items-center gap-1.5" aria-hidden>
              {CHAPTERS.map((chapter, index) => (
                <motion.span
                  key={chapter.title}
                  animate={{ width: index === active ? 28 : 8, opacity: index === active ? 1 : 0.35 }}
                  className="h-2 rounded-full bg-foreground"
                />
              ))}
            </div>
            <AnimatePresence mode="wait">
              <motion.div
                key={active}
                initial={{ opacity: 0, y: 16, filter: "blur(8px)" }}
                animate={{ opacity: 1, y: 0, filter: "blur(0px)" }}
                exit={{ opacity: 0, y: -12, filter: "blur(8px)" }}
                transition={{ type: "spring", stiffness: 260, damping: 28 }}
              >
                <Demo active />
              </motion.div>
            </AnimatePresence>
          </div>
        </div>
      </div>
    </div>
  );
}

function Chapter({
  index,
  title,
  body,
  Demo,
  active,
  onEnter,
}: (typeof CHAPTERS)[number] & { index: number; active: boolean; onEnter: () => void }) {
  const ref = useRef<HTMLElement>(null);
  const inView = useInView(ref, { amount: 0.6 });

  useEffect(() => {
    if (inView) {
      onEnter();
    }
  }, [inView, onEnter]);

  return (
    <article ref={ref} className="flex flex-col justify-center py-10 md:min-h-[62vh] md:py-0">
      <motion.div animate={{ opacity: active ? 1 : 0.35 }} transition={{ duration: 0.4 }} className="max-md:!opacity-100">
        <span
          className={cn(
            "mb-4 inline-flex size-9 items-center justify-center rounded-xl font-display font-bold transition-colors duration-500",
            active ? "bg-[image:var(--thermal)] text-white" : "bg-foreground/[0.07]",
          )}
        >
          {index + 1}
        </span>
        <h3 className="type-title text-3xl md:text-4xl">{title}</h3>
        <p className="mt-4 max-w-md text-lg leading-relaxed text-muted-foreground">{body}</p>
      </motion.div>
      <div className="mt-8 md:hidden">
        <div className="glass-strong rounded-3xl p-4">
          <Demo active={inView} />
        </div>
      </div>
    </article>
  );
}
