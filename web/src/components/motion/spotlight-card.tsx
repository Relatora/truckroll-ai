"use client";

import { useRef, type ComponentProps, type PointerEvent } from "react";
import { cn } from "@/lib/utils";

/**
 * A glass card that lights up under the pointer: a soft glow where the cursor is and a brighter
 * edge nearby. It only writes two CSS variables on pointer move; the `spotlight` utility in
 * globals.css does the painting, so it never re-renders React.
 */
export function SpotlightCard({ className, onPointerMove, ...props }: ComponentProps<"div">) {
  const ref = useRef<HTMLDivElement>(null);

  function track(event: PointerEvent<HTMLDivElement>) {
    const element = ref.current;
    if (element) {
      const rect = element.getBoundingClientRect();
      element.style.setProperty("--mx", `${event.clientX - rect.left}px`);
      element.style.setProperty("--my", `${event.clientY - rect.top}px`);
    }
    onPointerMove?.(event);
  }

  return <div ref={ref} onPointerMove={track} className={cn("spotlight glass rounded-2xl", className)} {...props} />;
}
