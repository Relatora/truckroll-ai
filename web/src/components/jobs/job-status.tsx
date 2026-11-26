"use client";

import { motion } from "motion/react";
import { Check } from "lucide-react";
import type { JobStatus } from "@/lib/customers";
import { statusLabels } from "@/lib/jobs";
import { cn } from "@/lib/utils";

const BADGE: Record<JobStatus, string> = {
  New: "bg-foreground/[0.06] text-foreground/80",
  Scheduled: "bg-warning-soft text-warning",
  Dispatched: "bg-frost-soft text-frost",
  InProgress: "bg-violet-soft text-violet",
  Completed: "bg-success-soft text-success",
  Cancelled: "bg-foreground/[0.06] text-muted-foreground line-through",
};

export function StatusBadge({ status, className }: { status: JobStatus; className?: string }) {
  return (
    <span className={cn("inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap", BADGE[status], className)}>
      {status === "InProgress" && <span className="size-1.5 animate-pulse rounded-full bg-violet" />}
      {statusLabels[status]}
    </span>
  );
}

const STEPS: JobStatus[] = ["Scheduled", "Dispatched", "InProgress", "Completed"];

/**
 * Where a job is in its life, as a track that fills from booked to done. The fill springs to the
 * new step when the status changes, so a change is something you see happen.
 */
export function JobStepper({ status }: { status: JobStatus }) {
  if (status === "Cancelled") {
    return <p className="rounded-2xl bg-foreground/[0.04] px-4 py-3 text-sm text-muted-foreground">This job was cancelled.</p>;
  }

  const reached = status === "New" ? -1 : STEPS.indexOf(status);
  return (
    <ol className="grid grid-cols-4 gap-2" aria-label="Progress">
      {STEPS.map((step, index) => {
        const done = index <= reached;
        return (
          <li key={step} className="space-y-2" aria-current={index === reached ? "step" : undefined}>
            <div className="h-1.5 overflow-hidden rounded-full bg-foreground/[0.08]">
              <motion.div
                initial={false}
                animate={{ width: done ? "100%" : "0%" }}
                transition={{ type: "spring", stiffness: 140, damping: 22, delay: index * 0.08 }}
                className={cn("h-full rounded-full", index === reached && status !== "Completed" ? "bg-[image:var(--thermal)]" : "bg-success")}
              />
            </div>
            <p className={cn("flex items-center gap-1 text-xs font-medium", done ? "text-foreground" : "text-muted-foreground")}>
              {done && index < reached && <Check className="size-3 text-success" />}
              {statusLabels[step]}
            </p>
          </li>
        );
      })}
    </ol>
  );
}
