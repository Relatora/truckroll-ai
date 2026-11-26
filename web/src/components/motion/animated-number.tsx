"use client";

import { animate, useInView, useReducedMotion } from "motion/react";
import { useEffect, useRef } from "react";

/**
 * A number that counts up to its value when it scrolls into view. Writes straight to the DOM
 * node on each frame, so counting never re-renders React. Screen readers get the final value.
 */
export function AnimatedNumber({ value, format = (n) => Math.round(n).toLocaleString("en-CA"), className }: { value: number; format?: (n: number) => string; className?: string }) {
  const ref = useRef<HTMLSpanElement>(null);
  const inView = useInView(ref, { once: true });
  const reduced = useReducedMotion();
  // Callers pass inline formatters; keep the latest one without restarting the count each render.
  const formatRef = useRef(format);
  useEffect(() => {
    formatRef.current = format;
  });

  useEffect(() => {
    const format = formatRef.current;
    const node = ref.current;
    if (!node || !inView) {
      return;
    }
    if (reduced) {
      node.textContent = format(value);
      return;
    }
    const controls = animate(0, value, {
      duration: 1.1,
      ease: [0.16, 1, 0.3, 1],
      onUpdate: (latest) => {
        node.textContent = format(latest);
      },
    });
    return () => controls.stop();
  }, [inView, reduced, value]);

  return (
    <span ref={ref} className={`tabular ${className ?? ""}`} aria-label={format(value)}>
      {format(0)}
    </span>
  );
}
