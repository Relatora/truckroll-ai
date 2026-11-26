import { ExternalLink } from "lucide-react";
import { PageHeader } from "./page-header";

/**
 * A page (or part of one) that's planned but not built yet, linking to the issue that builds it.
 * Honest about the state of a work-in-progress demo, and it disappears as the milestone lands.
 * Pass `embedded` when it sits inside another page, so the page keeps a single h1.
 */
export function NotBuiltYet({
  title,
  description,
  issue,
  embedded = false,
}: {
  title: string;
  description: string;
  issue: number;
  embedded?: boolean;
}) {
  const card = (
    <div className="glass relative max-w-xl overflow-hidden rounded-2xl p-6">
      {/* A slow-moving hazard stripe along the top: work in progress, in the trade's own language. */}
      <div
        aria-hidden
        className="absolute inset-x-0 top-0 h-1.5 opacity-80"
        style={{
          backgroundImage: "repeating-linear-gradient(-45deg, var(--ember) 0 10px, transparent 10px 20px)",
          backgroundSize: "28px 100%",
          animation: "stripe-slide 1.6s linear infinite",
        }}
      />
      <div className="space-y-3">
        {embedded ? (
          <>
            <h2 className="type-title text-xl">{title}</h2>
            <p className="text-sm text-muted-foreground">{description}</p>
          </>
        ) : (
          <p className="font-display text-lg font-semibold">This page is next on the build list.</p>
        )}
        <p className="text-sm text-muted-foreground">
          It&apos;s planned and tracked in the open, with the design discussion on the issue.
        </p>
        <a
          href={`https://github.com/Relatora/truckroll-ai/issues/${issue}`}
          target="_blank"
          rel="noreferrer"
          className="inline-flex w-fit items-center gap-1.5 rounded-full bg-foreground/[0.06] px-3 py-1.5 text-sm font-medium transition-colors hover:bg-foreground/10"
        >
          Issue #{issue} on GitHub
          <ExternalLink className="size-3.5" aria-hidden />
        </a>
      </div>
    </div>
  );

  if (embedded) {
    return card;
  }

  return (
    <div className="mx-auto w-full max-w-5xl">
      <PageHeader title={title} description={description} />
      {card}
    </div>
  );
}
