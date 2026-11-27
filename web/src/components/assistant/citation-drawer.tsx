"use client";

import { ArrowUpRight, BookOpen, Flame, Wrench } from "lucide-react";
import Link from "next/link";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import type { Citation, PassageSource } from "@/lib/assistant";
import { RichText } from "./rich-text";

export const sourceKinds: Record<PassageSource, { label: string; icon: typeof BookOpen; open: string }> = {
  Document: { label: "Manual or policy", icon: BookOpen, open: "" },
  Equipment: { label: "Equipment record", icon: Flame, open: "Open the customer" },
  Job: { label: "Job history", icon: Wrench, open: "Open the job" },
};

/**
 * The passage behind a citation, in full, in a side panel: what the model read, so the person
 * can check the answer against it. Records link to where they live in the CRM.
 */
export function CitationDrawer({ citation, onClose }: { citation: Citation | null; onClose: () => void }) {
  const kind = citation ? sourceKinds[citation.source] : null;
  const path = citation?.title.split(" > ") ?? [];

  return (
    <Sheet open={citation !== null} onOpenChange={(open) => !open && onClose()}>
      <SheetContent side="right" className="glass-strong w-full gap-0 border-l-0 bg-background/90 sm:max-w-lg">
        {citation && kind && (
          <>
            <SheetHeader className="gap-3 border-b border-foreground/[0.07] p-6 pr-12">
              <p className="flex items-center gap-2 text-sm text-muted-foreground">
                <span className="flex size-6 items-center justify-center rounded-lg bg-violet text-xs font-bold text-white">{citation.number}</span>
                <kind.icon className="size-4" />
                {kind.label}
              </p>
              <SheetTitle className="type-title text-xl">{path.at(-1)}</SheetTitle>
              {path.length > 1 && <SheetDescription>{path.slice(0, -1).join(", ")}</SheetDescription>}
              {path.length === 1 && <SheetDescription className="sr-only">Source {citation.number}</SheetDescription>}
            </SheetHeader>
            <div className="flex-1 overflow-y-auto p-6 text-[0.95rem]">
              <RichText text={citation.text} />
            </div>
            {citation.link && (
              <div className="border-t border-foreground/[0.07] p-4">
                <Link
                  href={citation.link}
                  onClick={onClose}
                  className="sheen inline-flex h-10 items-center gap-1.5 rounded-full bg-primary px-4 text-sm font-semibold text-primary-foreground"
                >
                  {kind.open} <ArrowUpRight className="size-4" />
                </Link>
              </div>
            )}
          </>
        )}
      </SheetContent>
    </Sheet>
  );
}
