import { cn } from "@/lib/utils";

/**
 * The ambient light behind every screen: four large, blurred lights in the trade colours,
 * drifting on long loops, under a fine film grain.
 *
 * `heat` warms it. The app passes "high" when there are open emergencies, so even the background
 * says something true about the business. `calm` turns it down for dense working screens.
 *
 * Pure CSS (keyframes on transforms only), so it costs no JavaScript and the GPU does the work.
 * Under reduced motion the lights stand still.
 */
export function Aurora({
  heat = "normal",
  calm = false,
  className,
}: {
  heat?: "normal" | "high";
  calm?: boolean;
  className?: string;
}) {
  const strength = calm ? "calc(var(--aurora-strength) * 0.55)" : "var(--aurora-strength)";

  return (
    <div
      aria-hidden
      className={cn("pointer-events-none fixed inset-0 -z-10 overflow-hidden", className)}
      style={{ opacity: strength }}
    >
      <Light color="var(--aurora-a)" className="top-[-20%] left-[-10%] size-[60vmax]" animation="drift-a 26s" boost={heat === "high"} />
      <Light color="var(--aurora-b)" className="top-[-10%] right-[-20%] size-[55vmax]" animation="drift-b 32s" />
      <Light color="var(--aurora-c)" className="bottom-[-30%] left-[20%] size-[50vmax]" animation="drift-c 38s" />
      <Light color="var(--aurora-d)" className="right-[-10%] bottom-[-20%] size-[40vmax]" animation="drift-d 30s" />
      {/* Film grain keeps the gradients from banding and gives the glass something to blur. */}
      <div
        className="absolute inset-0"
        style={{
          opacity: "var(--grain-opacity)",
          backgroundImage:
            "url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='160' height='160'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.85' numOctaves='3' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)'/%3E%3C/svg%3E\")",
        }}
      />
    </div>
  );
}

function Light({ color, className, animation, boost = false }: { color: string; className: string; animation: string; boost?: boolean }) {
  return (
    <div
      // No CSS blur: a radial gradient is already soft, and blurring a screen-sized element every
      // frame cost the page most of its frame rate. will-change keeps each light on its own layer,
      // so drifting it is a cheap transform.
      className={cn("absolute rounded-full transition-opacity duration-[2s] will-change-transform", className)}
      style={{
        background: `radial-gradient(circle, color-mix(in oklch, ${color} 85%, transparent) 0%, color-mix(in oklch, ${color} 30%, transparent) 35%, transparent 68%)`,
        animation: `${animation} ease-in-out infinite`,
        opacity: boost ? 1 : 0.8,
      }}
    />
  );
}
