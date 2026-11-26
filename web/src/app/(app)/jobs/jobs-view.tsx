"use client";

import { motion } from "motion/react";
import { ChevronLeft, ChevronRight, ClipboardList, Search, X } from "lucide-react";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { Segmented } from "@/components/controls/segmented";
import { BookJobDialog } from "@/components/jobs/book-job-dialog";
import { StatusBadge } from "@/components/jobs/job-status";
import { PageHeader } from "@/components/shell/page-header";
import { equipmentTone } from "@/lib/customers";
import { useJobs, type JobListItem } from "@/lib/jobs";
import { useMe } from "@/lib/session";
import { cn } from "@/lib/utils";

const DAYS = [
  { value: "today", label: "Today" },
  { value: "upcoming", label: "Upcoming" },
  { value: "past", label: "Past" },
  { value: "all", label: "All" },
];

const STATUSES = [
  { value: "", label: "Any status" },
  { value: "open", label: "Still open" },
  { value: "Scheduled", label: "Not assigned" },
  { value: "Dispatched", label: "Assigned" },
  { value: "InProgress", label: "On site" },
  { value: "Completed", label: "Done" },
];

const DOTS = { ember: "bg-ember", frost: "bg-frost", aqua: "bg-aqua", violet: "bg-violet" } as const;

/** Every job in scope, by day. Filters live in the URL, like the customer list. */
export function JobsView() {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const { data: me } = useMe();

  const filters = {
    day: params.get("day") ?? "today",
    status: params.get("status") ?? "",
    q: params.get("q") ?? "",
    page: Number(params.get("page") ?? "1"),
  };
  const { data, isPending, isFetching } = useJobs(filters);

  const [search, setSearch] = useState(filters.q);
  useEffect(() => {
    const handle = setTimeout(() => {
      if (search !== filters.q) update({ q: search, page: "" });
    }, 250);
    return () => clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search]);

  function update(changes: Record<string, string>) {
    const next = new URLSearchParams(params);
    Object.entries(changes).forEach(([key, value]) => (value ? next.set(key, value) : next.delete(key)));
    router.replace(`${pathname}${next.size ? `?${next}` : ""}`, { scroll: false });
  }

  const pageCount = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <div className="mx-auto w-full max-w-6xl">
      <PageHeader
        title="Jobs"
        description={me?.role === "Technician" ? "Your jobs, from today's route to everything you've done." : "Book, find and follow every job from the first call to the invoice."}
        actions={me?.can.canBookJobs ? <BookJobDialog /> : undefined}
      />

      <div className="mb-5 flex flex-wrap items-center gap-3">
        <Segmented id="day" label="Day" options={DAYS} value={filters.day} onChange={(value) => update({ day: value === "today" ? "" : value, page: "" })} />
        <label className="glass flex h-11 items-center gap-2 rounded-full pr-2 pl-4 text-sm focus-within:ring-3 focus-within:ring-ring/50">
          <span className="text-muted-foreground">Status</span>
          <select value={filters.status} onChange={(e) => update({ status: e.target.value, page: "" })} className="h-full cursor-pointer bg-transparent pr-1 font-medium outline-none">
            {STATUSES.map((s) => (
              <option key={s.value} value={s.value}>
                {s.label}
              </option>
            ))}
          </select>
        </label>
        <label className="glass flex h-11 min-w-[14rem] flex-1 items-center gap-2.5 rounded-full px-4 focus-within:ring-3 focus-within:ring-ring/50 sm:max-w-xs">
          <Search className="size-4 text-muted-foreground" aria-hidden />
          <span className="sr-only">Search jobs</span>
          <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Job number, customer or street" className="h-full flex-1 bg-transparent text-sm outline-none placeholder:text-muted-foreground" />
          {search && (
            <button type="button" onClick={() => setSearch("")} aria-label="Clear the search" className="rounded-full p-1 text-muted-foreground hover:bg-foreground/[0.06]">
              <X className="size-3.5" />
            </button>
          )}
        </label>
      </div>

      <div className={cn("space-y-2 transition-opacity duration-300", isFetching && !isPending && "opacity-60")} aria-busy={isFetching}>
        {isPending ? (
          Array.from({ length: 6 }, (_, i) => <div key={i} className="glass h-20 animate-pulse rounded-2xl" style={{ animationDelay: `${i * 70}ms` }} />)
        ) : data && data.items.length > 0 ? (
          <motion.ul initial="hidden" animate="shown" key={`${filters.day}-${filters.status}-${filters.page}`} variants={{ shown: { transition: { staggerChildren: 0.03 } } }} className="space-y-2">
            {data.items.map((job) => (
              <JobRow key={job.id} job={job} showDate={filters.day !== "today"} />
            ))}
          </motion.ul>
        ) : (
          <div className="glass flex flex-col items-center gap-3 rounded-3xl px-6 py-16 text-center">
            <span className="flex size-12 items-center justify-center rounded-2xl bg-foreground/[0.06]">
              <ClipboardList className="size-5" />
            </span>
            <p className="font-display text-lg font-semibold">No jobs here</p>
            <p className="max-w-sm text-sm text-muted-foreground">Try another day or status{me?.can.canBookJobs ? ", or book one" : ""}.</p>
          </div>
        )}
      </div>

      {data && data.total > data.pageSize && (
        <nav aria-label="Pages" className="mt-5 flex items-center justify-between text-sm text-muted-foreground">
          <span className="tabular">
            Page {data.page} of {pageCount}, {data.total.toLocaleString("en-CA")} jobs
          </span>
          <span className="flex gap-2">
            <button type="button" aria-label="Previous page" disabled={data.page <= 1} onClick={() => update({ page: String(data.page - 1) })} className="glass flex size-10 items-center justify-center rounded-full disabled:opacity-40">
              <ChevronLeft className="size-4" />
            </button>
            <button type="button" aria-label="Next page" disabled={data.page >= pageCount} onClick={() => update({ page: String(data.page + 1) })} className="glass flex size-10 items-center justify-center rounded-full disabled:opacity-40">
              <ChevronRight className="size-4" />
            </button>
          </span>
        </nav>
      )}
    </div>
  );
}

function JobRow({ job, showDate }: { job: JobListItem; showDate: boolean }) {
  const start = new Date(job.windowStart);
  const emergency = job.priority === "Emergency" && job.status !== "Completed" && job.status !== "Cancelled";
  return (
    <motion.li variants={{ hidden: { opacity: 0, y: 10 }, shown: { opacity: 1, y: 0, transition: { type: "spring", stiffness: 380, damping: 30 } } }}>
      <Link
        href={`/jobs/${job.id}`}
        className={cn(
          "group glass flex items-center gap-4 rounded-2xl px-4 py-3.5 transition-[transform,box-shadow] duration-300 ease-[var(--ease-spring)] hover:-translate-y-0.5 hover:shadow-lift focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none",
          emergency && "glow-ember",
        )}
      >
        <span className="w-20 shrink-0 text-center">
          {showDate && <span className="block text-xs text-muted-foreground">{start.toLocaleDateString("en-CA", { month: "short", day: "numeric" })}</span>}
          <span className="block font-display text-lg font-semibold tabular">{start.toLocaleTimeString("en-CA", { hour: "numeric" })}</span>
        </span>
        <span className={cn("h-10 w-1 shrink-0 rounded-full", DOTS[equipmentTone(job.equipmentKind)])} aria-hidden />
        <span className="min-w-0 flex-1">
          <span className="flex items-center gap-2">
            <span className="truncate font-medium">{job.summary}</span>
            {emergency && <span className="shrink-0 rounded-full bg-ember px-2 py-0.5 text-[0.7rem] font-bold text-white">Emergency</span>}
          </span>
          <span className="block truncate text-sm text-muted-foreground">
            {job.customer}, {job.address}
          </span>
        </span>
        <span className="hidden w-40 shrink-0 items-center gap-2 text-sm md:flex">
          {job.technician ? (
            <>
              <span className="size-2.5 rounded-full" style={{ background: job.technicianColor ?? undefined }} />
              <span className="truncate">{job.technician}</span>
            </>
          ) : (
            <span className="text-muted-foreground">Nobody yet</span>
          )}
        </span>
        <span className="hidden w-16 shrink-0 text-xs text-muted-foreground tabular sm:block">{job.number}</span>
        <StatusBadge status={job.status} />
      </Link>
    </motion.li>
  );
}
