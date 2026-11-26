import { cn } from "@/lib/utils";

/**
 * The Truckroll mark: a thermal tile (heat into cool, the trade in one gradient) holding a T,
 * beside the name in Bricolage. The same tile tops the dock in the app.
 */
export function Wordmark({ className }: { className?: string }) {
  return (
    <span className={cn("inline-flex items-center gap-2.5 leading-none", className)}>
      <span className="flex size-[1.6em] items-center justify-center rounded-[0.45em] bg-[image:var(--thermal)] text-[0.85em] font-bold text-white shadow-[0_6px_18px_-6px_var(--violet)]">
        T
      </span>
      <span className="font-display text-[1em] font-bold tracking-[-0.03em]">Truckroll</span>
    </span>
  );
}
