"use client";

import { motion } from "motion/react";
import { Ban, Check } from "lucide-react";
import { useState, type DragEvent } from "react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { equipmentTone } from "@/lib/customers";
import { holdsLicence, type Board, type BoardJob, type BoardTechnician } from "@/lib/dispatch";
import { cn } from "@/lib/utils";

const DAY_START = 8;
const DAY_HOURS = 8;
const at = (iso: string) => {
  const date = new Date(iso);
  return ((date.getHours() + date.getMinutes() / 60 - DAY_START) / DAY_HOURS) * 100;
};

const ROW = 44;

/**
 * Stacks jobs whose windows overlap into separate rows, so none hides another. Waiting jobs
 * often share a window (three calls for the noon slot); a technician's never should.
 */
function pack(jobs: BoardJob[]) {
  const rows: number[] = [];
  const placed = new Map<number, number>();
  for (const job of [...jobs].sort((a, b) => a.windowStart.localeCompare(b.windowStart))) {
    const start = new Date(job.windowStart).getTime();
    let row = rows.findIndex((end) => end <= start);
    if (row === -1) row = rows.push(0) - 1;
    rows[row] = new Date(job.windowEnd).getTime();
    placed.set(job.id, row);
  }
  return { rowOf: (id: number) => placed.get(id) ?? 0, rows: Math.max(1, rows.length) };
}

const TONE = {
  ember: "bg-ember-soft before:bg-ember",
  frost: "bg-frost-soft before:bg-frost",
  aqua: "bg-aqua-soft before:bg-aqua",
  violet: "bg-violet-soft before:bg-violet",
} as const;

const LICENCE_NAMES: Record<string, string> = { GasG1: "G1", GasG2: "G2", GasG3: "G3", Refrigeration313A: "313A", Plumber306A: "306A" };

/**
 * One lane per technician across the day, with "Waiting" on top. Drag a job onto a lane to give
 * it to that technician, or back to Waiting to take it off. While you drag, lanes that can legally
 * take the job glow and the rest dim with the licence they're missing. Every job is also a button:
 * Enter opens a menu with the same choices, for keyboards and touch.
 */
export function Lanes({
  board,
  selected,
  onSelect,
  onMove,
}: {
  board: Board;
  selected: number | null;
  onSelect: (jobId: number) => void;
  onMove: (job: BoardJob, technicianId: number | null) => void;
}) {
  const [dragging, setDragging] = useState<BoardJob | null>(null);
  const [over, setOver] = useState<string | null>(null);
  const waiting = board.jobs.filter((job) => job.technicianId === null);
  const waitingRows = pack(waiting);
  const nowPercent = currentPercent(board.date);

  function drop(event: DragEvent, technician: BoardTechnician | null) {
    event.preventDefault();
    setOver(null);
    const job = dragging;
    setDragging(null);
    if (!job || (job.technicianId ?? null) === (technician?.id ?? null)) return;
    onMove(job, technician?.id ?? null);
  }

  function laneProps(technician: BoardTechnician | null) {
    const key = technician ? String(technician.id) : "waiting";
    return {
      onDragOver: (event: DragEvent) => {
        if (!dragging) return;
        event.preventDefault();
        setOver(key);
      },
      onDragLeave: () => setOver((current) => (current === key ? null : current)),
      onDrop: (event: DragEvent) => drop(event, technician),
    };
  }

  return (
    <div className="glass overflow-x-auto rounded-3xl">
      <div className="min-w-[52rem] p-4 md:p-5">
        <div className="grid grid-cols-[11rem_1fr] gap-y-2">
          <div />
          <div className="relative h-6 text-xs font-medium text-muted-foreground">
            {[8, 10, 12, 14, 16].map((hour) => (
              <span
                key={hour}
                className={cn("absolute tabular whitespace-nowrap", hour === 8 ? "" : hour === 16 ? "-translate-x-full" : "-translate-x-1/2")}
                style={{ left: `${((hour - DAY_START) / DAY_HOURS) * 100}%` }}
              >
                {hour === 12 ? "noon" : `${hour > 12 ? hour - 12 : hour} ${hour < 12 ? "am" : "pm"}`}
              </span>
            ))}
          </div>

          <LaneLabel title="Waiting" detail={`${waiting.length} ${waiting.length === 1 ? "job" : "jobs"} to assign`} muted />
          <Track
            {...laneProps(null)}
            rows={waitingRows.rows}
            highlight={over === "waiting" ? "ok" : dragging && dragging.technicianId !== null ? "target" : undefined}
            dashed
            now={nowPercent}
          >
            {waiting.map((job) => (
              <Block key={job.id} job={job} row={waitingRows.rowOf(job.id)} board={board} selected={selected === job.id} onSelect={onSelect} onMove={onMove} onDrag={setDragging} />
            ))}
          </Track>

          {board.technicians.map((technician) => {
            const theirs = board.jobs.filter((job) => job.technicianId === technician.id);
            const packed = pack(theirs);
            const allowed = dragging ? holdsLicence(technician, dragging.requiredCertification) : true;
            const key = String(technician.id);
            return (
              <div key={technician.id} className="contents">
                <LaneLabel
                  title={technician.name}
                  detail={technician.visiting ? "Covering from another branch" : licences(technician)}
                  colour={technician.color}
                  warning={dragging && !allowed ? `No ${LICENCE_NAMES[dragging.requiredCertification] ?? "licence"}` : undefined}
                />
                <Track
                  {...(allowed ? laneProps(technician) : {})}
                  rows={packed.rows}
                  highlight={!dragging ? undefined : !allowed ? "blocked" : over === key ? "ok" : "target"}
                  now={nowPercent}
                >
                  {theirs.map((job) => (
                    <Block key={job.id} job={job} row={packed.rowOf(job.id)} board={board} selected={selected === job.id} onSelect={onSelect} onMove={onMove} onDrag={setDragging} />
                  ))}
                </Track>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}

function LaneLabel({ title, detail, colour, muted, warning }: { title: string; detail: string; colour?: string; muted?: boolean; warning?: string }) {
  return (
    <div className="sticky left-0 z-20 -ml-4 flex items-center gap-2.5 bg-card/90 pr-3 pl-4 md:static md:ml-0 md:bg-transparent md:pl-0">
      {colour && <span className="size-2.5 shrink-0 rounded-full" style={{ background: colour }} />}
      <span className="min-w-0">
        <span className={cn("block truncate text-sm font-semibold", muted && "text-muted-foreground")}>{title}</span>
        <span className={cn("block truncate text-xs", warning ? "font-semibold text-destructive" : "text-muted-foreground")}>{warning ?? detail}</span>
      </span>
    </div>
  );
}

function Track({
  children,
  highlight,
  dashed,
  now,
  rows,
  ...handlers
}: {
  children: React.ReactNode;
  highlight?: "target" | "ok" | "blocked";
  dashed?: boolean;
  now: number | null;
  rows: number;
} & React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      {...handlers}
      style={{ height: rows * ROW + 12 }}
      className={cn(
        "relative rounded-xl transition-[background-color,box-shadow,opacity] duration-200",
        dashed ? "border border-dashed border-foreground/15" : "bg-foreground/[0.035]",
        highlight === "target" && "bg-success-soft/60",
        highlight === "ok" && "bg-success-soft ring-2 ring-success",
        highlight === "blocked" && "opacity-40",
      )}
    >
      {children}
      {now !== null && <span aria-hidden className="pointer-events-none absolute -inset-y-1 z-10 w-0.5 rounded-full bg-foreground/50" style={{ left: `${now}%` }} />}
    </div>
  );
}

function Block({
  job,
  row,
  board,
  selected,
  onSelect,
  onMove,
  onDrag,
}: {
  job: BoardJob;
  row: number;
  board: Board;
  selected: boolean;
  onSelect: (jobId: number) => void;
  onMove: (job: BoardJob, technicianId: number | null) => void;
  onDrag: (job: BoardJob | null) => void;
}) {
  const movable = board.canDispatch && (job.status === "Scheduled" || job.status === "Dispatched");
  const emergency = job.priority === "Emergency" && job.status !== "Completed";
  const left = Math.max(0, at(job.windowStart));
  const width = Math.min(100 - left, at(job.windowEnd) - at(job.windowStart));

  const block = (
    <motion.button
      layoutId={`job-${job.id}`}
      type="button"
      draggable={movable}
      onDragStart={(event) => {
        const dragEvent = event as unknown as DragEvent;
        dragEvent.dataTransfer.effectAllowed = "move";
        dragEvent.dataTransfer.setData("text/plain", String(job.id));
        onDrag(job);
      }}
      onDragEnd={() => onDrag(null)}
      onClick={() => onSelect(job.id)}
      transition={{ type: "spring", stiffness: 300, damping: 28 }}
      className={cn(
        "absolute z-[5] flex items-center gap-1.5 overflow-hidden rounded-lg pr-2 pl-3.5 text-left text-[0.8rem] font-medium text-foreground outline-none before:absolute before:inset-y-1.5 before:left-1 before:w-1 before:rounded-full focus-visible:ring-3 focus-visible:ring-ring/50",
        emergency ? "bg-ember text-white glow-ember before:bg-white/70" : TONE[equipmentTone(job.equipmentKind)],
        job.status === "Completed" && "opacity-55",
        movable && "cursor-grab active:cursor-grabbing",
        selected && "ring-2 ring-foreground",
      )}
      style={{ left: `${left}%`, width: `${width}%`, top: 6 + row * ROW, height: ROW - 6 }}
      title={`${job.number}: ${job.summary} (${job.customer}, ${job.street})`}
    >
      {job.status === "Completed" && <Check className="size-3.5 shrink-0" />}
      {job.status === "InProgress" && <span className="size-1.5 shrink-0 animate-pulse rounded-full bg-violet" />}
      <span className="truncate">{job.street}</span>
    </motion.button>
  );

  if (!movable) {
    return block;
  }

  // The keyboard and touch way to do what dragging does.
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild onKeyDown={(event) => event.key === "Enter" && event.preventDefault()}>
        {block}
      </DropdownMenuTrigger>
      <DropdownMenuContent className="glass-strong w-64 rounded-xl">
        <DropdownMenuLabel className="font-normal">
          <span className="block font-semibold">{job.number}</span>
          <span className="block text-xs text-muted-foreground">{job.summary}</span>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        {board.technicians.map((technician) => {
          const allowed = holdsLicence(technician, job.requiredCertification);
          return (
            <DropdownMenuItem key={technician.id} disabled={!allowed || technician.id === job.technicianId} onSelect={() => onMove(job, technician.id)}>
              <span className="size-2 rounded-full" style={{ background: technician.color }} />
              <span className="flex-1">{technician.name}</span>
              {!allowed && (
                <span className="flex items-center gap-1 text-xs text-destructive">
                  <Ban className="size-3" /> {LICENCE_NAMES[job.requiredCertification]}
                </span>
              )}
            </DropdownMenuItem>
          );
        })}
        {job.technicianId !== null && (
          <>
            <DropdownMenuSeparator />
            <DropdownMenuItem onSelect={() => onMove(job, null)}>Back to waiting</DropdownMenuItem>
          </>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

function licences(technician: BoardTechnician) {
  const held = technician.certifications.split(",").map((c) => LICENCE_NAMES[c.trim()]).filter(Boolean);
  return held.length ? held.join(", ") : "No licences";
}

/** Where "now" falls on today's track, or null on any other day. */
function currentPercent(date: string) {
  const now = new Date();
  if (now.toLocaleDateString("en-CA") !== date) return null;
  const percent = ((now.getHours() + now.getMinutes() / 60 - DAY_START) / DAY_HOURS) * 100;
  return percent >= 0 && percent <= 100 ? percent : null;
}
