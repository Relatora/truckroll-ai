"use client";

import { LayoutGroup, motion } from "motion/react";
import { cn } from "@/lib/utils";

/** A pill row whose active highlight slides between options. */
export function Segmented({ id, label, options, value, onChange }: { id: string; label: string; options: { value: string; label: string }[]; value: string; onChange: (value: string) => void }) {
  return (
    <LayoutGroup id={id}>
      <div role="radiogroup" aria-label={label} className="glass flex h-11 max-w-full items-center gap-0.5 overflow-x-auto rounded-full p-1">
        {options.map((option) => {
          const active = option.value === value;
          return (
            <button
              key={option.value}
              type="button"
              role="radio"
              aria-checked={active}
              onClick={() => onChange(option.value)}
              className={cn("relative h-full shrink-0 rounded-full px-3.5 text-sm font-medium whitespace-nowrap transition-colors focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none", active ? "text-primary-foreground" : "text-muted-foreground hover:text-foreground")}
            >
              {active && <motion.span layoutId={`${id}-pill`} className="absolute inset-0 rounded-full bg-primary" transition={{ type: "spring", stiffness: 500, damping: 36 }} />}
              <span className="relative">{option.label}</span>
            </button>
          );
        })}
      </div>
    </LayoutGroup>
  );
}
