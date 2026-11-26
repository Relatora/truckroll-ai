"use client";

import { AnimatePresence, motion } from "motion/react";
import { MoonStar, Sun } from "lucide-react";
import { useTheme } from "next-themes";
import { type MouseEvent } from "react";
import { flushSync } from "react-dom";
import { useHydrated } from "@/lib/use-hydrated";
import { cn } from "@/lib/utils";

/**
 * Daylight / After hours. The new theme spreads out as a circle from the button, using the View
 * Transitions API where the browser has it (and an instant switch where it doesn't, or when the
 * person prefers reduced motion).
 */
export function ThemeSwitch({ className }: { className?: string }) {
  const { resolvedTheme, setTheme } = useTheme();
  // The theme is only known on the client; render a neutral button until then.
  const mounted = useHydrated();
  const dark = mounted && resolvedTheme === "dark";

  function toggle(event: MouseEvent<HTMLButtonElement>) {
    const next = dark ? "light" : "dark";
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    if (!document.startViewTransition || reduced) {
      setTheme(next);
      return;
    }

    const x = event.clientX;
    const y = event.clientY;
    const radius = Math.hypot(Math.max(x, innerWidth - x), Math.max(y, innerHeight - y));

    const transition = document.startViewTransition(() => {
      flushSync(() => setTheme(next));
    });

    transition.ready.then(() => {
      document.documentElement.animate(
        { clipPath: [`circle(0px at ${x}px ${y}px)`, `circle(${radius}px at ${x}px ${y}px)`] },
        { duration: 650, easing: "cubic-bezier(0.16, 1, 0.3, 1)", pseudoElement: "::view-transition-new(root)" },
      );
    });
  }

  return (
    <button
      type="button"
      onClick={toggle}
      aria-label={dark ? "Switch to the light theme" : "Switch to the dark theme"}
      className={cn(
        "glass relative inline-flex size-10 items-center justify-center rounded-full text-foreground transition-transform duration-200 ease-[var(--ease-spring)] hover:scale-105 active:scale-95 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none",
        className,
      )}
    >
      <AnimatePresence mode="wait" initial={false}>
        <motion.span
          key={dark ? "moon" : "sun"}
          initial={{ rotate: -90, scale: 0.4, opacity: 0 }}
          animate={{ rotate: 0, scale: 1, opacity: 1 }}
          exit={{ rotate: 90, scale: 0.4, opacity: 0 }}
          className="inline-flex"
        >
          {dark ? <MoonStar className="size-[18px]" /> : <Sun className="size-[18px]" />}
        </motion.span>
      </AnimatePresence>
    </button>
  );
}
