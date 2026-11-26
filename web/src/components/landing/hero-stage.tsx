"use client";

import { motion, useReducedMotion, useScroll, useSpring, useTransform } from "motion/react";
import { useRef } from "react";
import { HeroBoard } from "./hero-board";

/**
 * The board's stage. It arrives tipped back in 3D, like a screen on a desk, and straightens to
 * face you as you scroll it into view. Floating status chips orbit it on wide screens.
 */
export function HeroStage() {
  const ref = useRef<HTMLDivElement>(null);
  const reduced = useReducedMotion();
  const { scrollYProgress } = useScroll({ target: ref, offset: ["start end", "center center"] });
  const progress = useSpring(scrollYProgress, { stiffness: 120, damping: 24, mass: 0.4 });

  const rotateX = useTransform(progress, [0, 1], [26, 0]);
  const scale = useTransform(progress, [0, 1], [0.9, 1]);
  const y = useTransform(progress, [0, 1], [60, 0]);

  return (
    <div ref={ref} className="relative" style={{ perspective: "1800px" }}>
      <motion.div style={reduced ? undefined : { rotateX, scale, y, transformOrigin: "50% 0%" }}>
        <HeroBoard />
      </motion.div>

      {/* Chips sit mostly outside the board's edges, so they never cover a job. */}
      <Chip className="-top-5 left-0 -translate-x-1/3" delay="0s" tone="ember">
        Gas emergency, 14 Maple St
      </Chip>
      <Chip className="top-20 right-0 translate-x-2/3" delay="1.2s" tone="frost">
        Nadia finishes 3 km away
      </Chip>
      <Chip className="top-64 left-0 -translate-x-2/3" delay="2.1s" tone="violet">
        Licences checked first
      </Chip>
    </div>
  );
}

function Chip({ children, className, delay, tone }: { children: React.ReactNode; className: string; delay: string; tone: "ember" | "frost" | "violet" }) {
  const dot = { ember: "bg-ember", frost: "bg-frost", violet: "bg-violet" }[tone];
  return (
    <div
      aria-hidden
      className={`glass-strong absolute z-30 hidden items-center gap-2 rounded-full px-3.5 py-2 text-sm font-medium xl:flex ${className}`}
      style={{ animation: `float-y 6s ease-in-out ${delay} infinite` }}
    >
      <span className={`size-2 rounded-full ${dot}`} />
      {children}
    </div>
  );
}
