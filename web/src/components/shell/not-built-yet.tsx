import { ExternalLink } from "lucide-react";
import { PageHeader } from "./page-header";

/**
 * A page (or part of one) that's planned but not built yet, linking to the issue that builds it.
 * Honest about the state of a work-in-progress demo, and it disappears as the milestone lands.
 * Pass <c>embedded</c> when it sits inside another page, so the page keeps a single h1.
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
    <div className="flex max-w-xl flex-col gap-3 rounded-xl border border-dashed bg-card p-6">
      {embedded ? (
        <>
          <h2 className="type-title text-xl">{title}</h2>
          <p className="text-sm text-muted-foreground">{description}</p>
        </>
      ) : (
        <p className="font-medium">This page is next on the build list.</p>
      )}
      <p className="text-sm text-muted-foreground">
        It&apos;s planned and tracked in the open, with the design discussion on the issue.
      </p>
      <a
        href={`https://github.com/Relatora/truckroll-ai/issues/${issue}`}
        target="_blank"
        rel="noreferrer"
        className="inline-flex w-fit items-center gap-1.5 text-sm font-medium text-frost underline-offset-4 hover:underline"
      >
        Issue #{issue} on GitHub
        <ExternalLink className="size-3.5" aria-hidden />
      </a>
    </div>
  );

  if (embedded) {
    return card;
  }

  return (
    <>
      <PageHeader title={title} description={description} />
      {card}
    </>
  );
}
