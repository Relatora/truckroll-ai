import { cn } from "@/lib/utils";

/**
 * The Truckroll wordmark: the name set in Archivo at full width and weight, the way a company's
 * name is lettered on a truck door. The two bars underneath read as a road, or a day on the board.
 */
export function Wordmark({ className, inverted = false }: { className?: string; inverted?: boolean }) {
  return (
    <span className={cn("inline-flex flex-col gap-[0.18em] leading-none", className)}>
      <span className="type-display text-[1em]">Truckroll</span>
      <span aria-hidden className="flex h-[0.14em] gap-[0.18em]">
        <span className={cn("w-[62%] rounded-full", inverted ? "bg-sidebar-primary" : "bg-ember")} />
        <span className={cn("flex-1 rounded-full", inverted ? "bg-sidebar-foreground/40" : "bg-foreground/25")} />
      </span>
    </span>
  );
}
