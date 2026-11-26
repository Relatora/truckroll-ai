"use client";

import { AnimatePresence, motion } from "motion/react";
import { ChevronLeft, ChevronRight, MapPin, X } from "lucide-react";
import dynamic from "next/dynamic";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";
import { Lanes } from "@/components/dispatch/lanes";
import { StatusBadge } from "@/components/jobs/job-status";
import { PageHeader } from "@/components/shell/page-header";
import { ApiError } from "@/lib/api";
import { useBranches } from "@/lib/customers";
import { useBoard, useDispatchHub, useMoveJob, type BoardJob, type LiveState } from "@/lib/dispatch";
import { windowLabel } from "@/lib/jobs";
import { useMe } from "@/lib/session";
import { cn } from "@/lib/utils";

// MapLibre needs the browser; load it on the client only, after the lanes.
const BoardMap = dynamic(() => import("@/components/dispatch/board-map").then((m) => m.BoardMap), {
  ssr: false,
  loading: () => <div className="h-full w-full animate-pulse bg-foreground/[0.04]" />,
});

/**
 * The dispatch board: a branch's day on a map and in lanes, live for everyone watching.
 * The day and branch live in the URL (?date=&branch=).
 */
export function DispatchView() {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const { data: me } = useMe();
  const { data: branches } = useBranches();

  const today = new Date().toLocaleDateString("en-CA");
  const date = params.get("date") ?? today;
  const branch = params.get("branch") ?? "";
  const { data: board, isPending } = useBoard(date, branch);
  const move = useMoveJob(date, branch);
  const [selected, setSelected] = useState<number | null>(null);

  const live = useDispatchHub(board?.branch.code, (change) => {
    if (me && change.changedBy !== me.displayName) {
      toast(`${change.number} ${change.change}`, { description: `By ${change.changedBy}` });
    }
  });

  function update(changes: Record<string, string>) {
    const next = new URLSearchParams(params);
    Object.entries(changes).forEach(([key, value]) => (value ? next.set(key, value) : next.delete(key)));
    router.replace(`${pathname}${next.size ? `?${next}` : ""}`, { scroll: false });
  }

  function shift(days: number) {
    const next = new Date(`${date}T12:00:00`);
    next.setDate(next.getDate() + days);
    update({ date: next.toLocaleDateString("en-CA") === today ? "" : next.toLocaleDateString("en-CA") });
    setSelected(null);
  }

  async function onMove(job: BoardJob, technicianId: number | null) {
    const name = board?.technicians.find((t) => t.id === technicianId)?.name;
    try {
      await move.mutateAsync({ jobId: job.id, technicianId });
      toast.success(technicianId === null ? `${job.number} is back in Waiting` : `Assigned ${job.number} to ${name}`);
    } catch (error) {
      toast.error(error instanceof ApiError ? error.message : "Couldn't move the job.", { description: "It's back where it was." });
    }
  }

  const job = board?.jobs.find((j) => j.id === selected) ?? null;
  const label = new Date(`${date}T12:00:00`).toLocaleDateString("en-CA", { weekday: "long", month: "long", day: "numeric" });

  return (
    <div className="mx-auto w-full max-w-7xl">
      <PageHeader
        title="Dispatch board"
        description={board ? `${board.branch.name}, ${label}.` : label}
        actions={<LiveBadge state={live} />}
      />

      <div className="mb-5 flex flex-wrap items-center gap-3">
        <div className="glass flex h-11 items-center gap-1 rounded-full p-1">
          <button type="button" aria-label="Previous day" onClick={() => shift(-1)} className="flex size-9 items-center justify-center rounded-full hover:bg-foreground/[0.06]">
            <ChevronLeft className="size-4" />
          </button>
          <button type="button" onClick={() => update({ date: "" })} className={cn("h-9 rounded-full px-4 text-sm font-medium", date === today ? "bg-primary text-primary-foreground" : "hover:bg-foreground/[0.06]")}>
            Today
          </button>
          <button type="button" aria-label="Next day" onClick={() => shift(1)} className="flex size-9 items-center justify-center rounded-full hover:bg-foreground/[0.06]">
            <ChevronRight className="size-4" />
          </button>
        </div>
        {me?.role === "Owner" && branches && (
          <label className="glass flex h-11 items-center gap-2 rounded-full pr-2 pl-4 text-sm">
            <span className="text-muted-foreground">Branch</span>
            <select value={board?.branch.code ?? branch} onChange={(e) => update({ branch: e.target.value })} className="h-full bg-transparent font-medium outline-none">
              {branches.map((b) => (
                <option key={b.code} value={b.code}>
                  {b.name}
                </option>
              ))}
            </select>
          </label>
        )}
        {board && !board.canDispatch && <p className="text-sm text-muted-foreground">You can see the board; dispatchers and managers assign jobs.</p>}
      </div>

      {isPending || !board ? (
        <div className="glass h-[32rem] animate-pulse rounded-3xl" />
      ) : (
        <div className="space-y-5">
          <div className="glass relative h-[22rem] overflow-hidden rounded-3xl md:h-[26rem]">
            <BoardMap board={board} selected={selected} onSelect={setSelected} />
            <AnimatePresence>{job && <JobCard job={job} onClose={() => setSelected(null)} technician={board.technicians.find((t) => t.id === job.technicianId)?.name} />}</AnimatePresence>
          </div>
          <Lanes board={board} selected={selected} onSelect={setSelected} onMove={onMove} />
        </div>
      )}
    </div>
  );
}

function JobCard({ job, technician, onClose }: { job: BoardJob; technician?: string; onClose: () => void }) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 16, scale: 0.97 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, y: 16, scale: 0.97 }}
      className="glass-strong absolute bottom-3 left-3 z-10 w-[min(22rem,calc(100%-1.5rem))] rounded-2xl p-4"
    >
      <button type="button" onClick={onClose} aria-label="Close" className="absolute top-3 right-3 rounded-full p-1 text-muted-foreground hover:bg-foreground/[0.06]">
        <X className="size-4" />
      </button>
      <p className="flex items-center gap-2 text-xs text-muted-foreground">
        <span className="font-semibold text-foreground tabular">{job.number}</span>
        <StatusBadge status={job.status} />
      </p>
      <p className="mt-2 pr-6 font-medium">{job.summary}</p>
      <p className="mt-1 flex items-center gap-1 text-sm text-muted-foreground">
        <MapPin className="size-3.5" /> {job.customer}, {job.street}
      </p>
      <p className="mt-1 text-sm text-muted-foreground">
        {windowLabel(job.windowStart, job.windowEnd)}, {technician ?? "waiting for a technician"}
      </p>
      <Link href={`/jobs/${job.id}`} className="mt-3 inline-block text-sm font-semibold underline decoration-foreground/25 underline-offset-4 hover:decoration-foreground">
        Open the job
      </Link>
    </motion.div>
  );
}

function LiveBadge({ state }: { state: LiveState }) {
  const styles: Record<LiveState, [string, string]> = {
    connecting: ["bg-warning", "Connecting"],
    live: ["bg-success", "Live"],
    reconnecting: ["bg-warning", "Reconnecting"],
    offline: ["bg-destructive", "Offline: refresh to retry"],
  };
  const [dot, text] = styles[state];
  return (
    <span className="glass inline-flex h-9 items-center gap-2 rounded-full px-3.5 text-sm font-medium" aria-live="polite">
      <span className="relative flex size-2">
        {state === "live" && <span className={cn("absolute inset-0 rounded-full", dot)} style={{ animation: "pulse-ring 1.8s ease-out infinite" }} />}
        <span className={cn("relative size-2 rounded-full", dot)} />
      </span>
      {text}
    </span>
  );
}
