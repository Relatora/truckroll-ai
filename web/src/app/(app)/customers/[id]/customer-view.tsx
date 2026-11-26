"use client";

import { motion } from "motion/react";
import {
  ArrowLeft,
  BookOpen,
  Check,
  CircleAlert,
  Droplets,
  Flame,
  KeyRound,
  Mail,
  Phone,
  ShieldCheck,
  Snowflake,
  UserRound,
  Wind,
} from "lucide-react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { AnimatedNumber } from "@/components/motion/animated-number";
import { Reveal, RevealItem } from "@/components/motion/reveal";
import { SpotlightCard } from "@/components/motion/spotlight-card";
import { BookJobDialog } from "@/components/jobs/book-job-dialog";
import { toneStyles } from "@/components/shell/navigation";
import { ApiError } from "@/lib/api";
import {
  equipmentLabels,
  equipmentTone,
  fromNow,
  planLabels,
  useCustomer,
  type CustomerDetail,
  type EquipmentDetail,
  type EquipmentKind,
  type TimelineJob,
} from "@/lib/customers";
import { useMe } from "@/lib/session";
import { cn } from "@/lib/utils";

/**
 * The Customer 360: everything about one home on one screen, for the CSR on the phone with them.
 * What's installed and whether it's covered come first, then the history, newest first.
 */
export function CustomerView() {
  const { id } = useParams<{ id: string }>();
  const { data: customer, error, isPending } = useCustomer(Number(id));

  if (isPending) {
    return <Loading />;
  }

  if (error || !customer) {
    return <NotFound missing={error instanceof ApiError && error.status === 404} />;
  }

  return (
    <Reveal className="mx-auto w-full max-w-6xl">
      <RevealItem>
        <Link href="/customers" className="mb-6 inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-sm font-medium text-muted-foreground transition-colors hover:bg-foreground/[0.06] hover:text-foreground">
          <ArrowLeft className="size-4" /> Customers
        </Link>
      </RevealItem>

      <RevealItem>
        <Header customer={customer} />
      </RevealItem>

      <RevealItem className="mt-8 grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Stat label="Visits" value={customer.stats.visits} />
        <Stat label="Lifetime revenue" value={customer.stats.lifetimeRevenue} format={(n) => `$${Math.round(n).toLocaleString("en-CA")}`} />
        <Stat
          label="Fixed on the first visit"
          value={customer.stats.firstTimeFixRate === null ? null : customer.stats.firstTimeFixRate * 100}
          format={(n) => `${Math.round(n)}%`}
        />
        <Stat label="Open jobs" value={customer.stats.openJobs} hot={customer.stats.openJobs > 0} />
      </RevealItem>

      <div className="mt-12 grid gap-10 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.15fr)]">
        <section aria-labelledby="equipment-heading">
          <RevealItem>
            <h2 id="equipment-heading" className="type-title text-2xl">
              What&apos;s installed
            </h2>
          </RevealItem>
          {customer.properties.map((property) => (
            <div key={property.id} className="mt-4 space-y-3">
              {property.accessNotes && (
                <RevealItem>
                  <p className="glass flex gap-3 rounded-2xl p-4 text-sm">
                    <KeyRound className="mt-0.5 size-4 shrink-0 text-warning" aria-hidden />
                    <span>
                      <span className="font-semibold">Getting in.</span> {property.accessNotes}
                    </span>
                  </p>
                </RevealItem>
              )}
              {property.equipment.map((item) => (
                <RevealItem key={item.id}>
                  <EquipmentCard item={item} />
                </RevealItem>
              ))}
            </div>
          ))}
        </section>

        <section aria-labelledby="history-heading">
          <RevealItem>
            <h2 id="history-heading" className="type-title text-2xl">
              History
            </h2>
          </RevealItem>
          <Timeline jobs={customer.timeline} />
        </section>
      </div>
    </Reveal>
  );
}

function Header({ customer }: { customer: CustomerDetail }) {
  const { data: me } = useMe();
  const home = customer.properties[0];
  const initials = customer.firstName[0] + customer.lastName[0];
  return (
    <div className="flex flex-wrap items-end justify-between gap-6">
      <div className="flex items-center gap-5">
        <motion.span
          initial={{ scale: 0.6, rotate: -12 }}
          animate={{ scale: 1, rotate: 0 }}
          transition={{ type: "spring", stiffness: 260, damping: 16 }}
          className="flex size-16 shrink-0 items-center justify-center rounded-2xl bg-[image:var(--thermal)] font-display text-2xl font-bold text-white shadow-[0_12px_30px_-10px_var(--violet)]"
        >
          {initials}
        </motion.span>
        <div>
          <h1 className="type-display text-4xl md:text-5xl">
            {customer.firstName} {customer.lastName}
          </h1>
          <p className="mt-2 text-muted-foreground">
            {home ? `${home.street}, ${home.city} ${home.postalCode}` : "No address on file"}. Customer since{" "}
            {new Date(customer.customerSince).getFullYear()}, {customer.branch.name} branch.
          </p>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        {customer.membership && (
          <span
            className={cn(
              "inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-sm font-semibold",
              customer.membership.status === "Active" ? "bg-success-soft text-success" : "bg-warning-soft text-warning",
            )}
          >
            <ShieldCheck className="size-4" />
            {planLabels[customer.membership.plan]}
            {customer.membership.status !== "Active" && ` (${customer.membership.status.toLowerCase()})`}
          </span>
        )}
        <a href={`tel:${customer.phone}`} className="glass inline-flex h-10 items-center gap-2 rounded-full px-4 text-sm font-medium transition-transform duration-200 ease-[var(--ease-spring)] hover:-translate-y-0.5">
          <Phone className="size-4" /> <span className="tabular">{customer.phone}</span>
        </a>
        <a href={`mailto:${customer.email}`} aria-label={`Email ${customer.email}`} className="glass inline-flex size-10 items-center justify-center rounded-full transition-transform duration-200 ease-[var(--ease-spring)] hover:-translate-y-0.5">
          <Mail className="size-4" />
        </a>
        {me?.can.canBookJobs && <BookJobDialog customerId={customer.id} />}
      </div>
    </div>
  );
}

function Stat({ label, value, format, hot = false }: { label: string; value: number | null; format?: (n: number) => string; hot?: boolean }) {
  return (
    <SpotlightCard className={cn("p-5", hot && "glow-ember")}>
      <p className="text-sm text-muted-foreground">{label}</p>
      <p className={cn("mt-2 font-display text-3xl font-semibold tracking-tight", hot && "text-ember")}>
        {value === null ? "–" : <AnimatedNumber value={value} format={format} />}
      </p>
    </SpotlightCard>
  );
}

const KIND_ICONS: Record<ReturnType<typeof equipmentTone>, typeof Flame> = {
  ember: Flame,
  frost: Snowflake,
  aqua: Droplets,
  violet: Wind,
};

function EquipmentCard({ item }: { item: EquipmentDetail }) {
  const tone = equipmentTone(item.kind);
  const Icon = KIND_ICONS[tone];

  return (
    <SpotlightCard className="group flex items-center gap-4 p-4">
      <span className={cn("flex size-12 shrink-0 items-center justify-center rounded-xl transition-transform duration-300 ease-[var(--ease-spring)] group-hover:-rotate-6 group-hover:scale-110", toneStyles[tone])}>
        <Icon className="size-5" />
      </span>
      <div className="min-w-0 flex-1">
        <p className="text-sm text-muted-foreground">{equipmentLabels[item.kind]}</p>
        <p className="truncate font-display text-lg font-semibold tracking-tight">
          {item.brand} {item.model}
        </p>
        <p className="mt-0.5 flex flex-wrap gap-x-3 gap-y-0.5 text-xs text-muted-foreground">
          <span className="tabular">{item.serialNumber}</span>
          <span>
            Installed {new Date(item.installedOn).getFullYear()}, {item.ageYears} {item.ageYears === 1 ? "year" : "years"} old
          </span>
          {item.installedByUs && <span className="font-medium text-foreground/80">Installed by us</span>}
          {item.manualCode && (
            <span className="inline-flex items-center gap-1">
              <BookOpen className="size-3" /> {item.manualCode}
            </span>
          )}
        </p>
      </div>
      <WarrantyRing item={item} />
    </SpotlightCard>
  );
}

/**
 * How much warranty is left, as a ring that fills on arrival. Green while covered, amber in the
 * last 90 days (worth a call: a repair now is free), red once expired, grey when nobody knows.
 */
function WarrantyRing({ item }: { item: EquipmentDetail }) {
  const radius = 17;
  const circumference = 2 * Math.PI * radius;
  const fraction = item.warrantyLeft ?? 0;

  const styles = {
    Covered: { color: "var(--success)", label: "Covered" },
    Expiring: { color: "var(--warning)", label: "Ends soon" },
    Expired: { color: "var(--destructive)", label: "Expired" },
    Unknown: { color: "var(--muted-foreground)", label: "Unknown" },
  }[item.warranty];

  const until = item.warrantyExpiresOn ? new Date(item.warrantyExpiresOn).toLocaleDateString("en-CA", { month: "short", year: "numeric" }) : null;

  return (
    <div className="flex shrink-0 flex-col items-center gap-1" title={until ? `Warranty until ${until}` : "Warranty unknown"}>
      <svg viewBox="0 0 40 40" className="size-11 -rotate-90" aria-hidden>
        <circle cx="20" cy="20" r={radius} fill="none" stroke="currentColor" strokeWidth="3.5" className="text-foreground/[0.08]" />
        <motion.circle
          cx="20"
          cy="20"
          r={radius}
          fill="none"
          stroke={styles.color}
          strokeWidth="3.5"
          strokeLinecap="round"
          strokeDasharray={circumference}
          initial={{ strokeDashoffset: circumference }}
          animate={{ strokeDashoffset: circumference * (1 - fraction) }}
          transition={{ type: "spring", stiffness: 60, damping: 18, delay: 0.3 }}
        />
      </svg>
      <span className="text-[0.7rem] font-semibold" style={{ color: styles.color }}>
        {styles.label}
      </span>
    </div>
  );
}

const STATUS_LABELS: Record<TimelineJob["status"], string> = {
  New: "New",
  Scheduled: "Scheduled",
  Dispatched: "Assigned",
  InProgress: "On site",
  Completed: "Done",
  Cancelled: "Cancelled",
};

function Timeline({ jobs }: { jobs: TimelineJob[] }) {
  const [showAll, setShowAll] = useState(false);
  const visible = showAll ? jobs : jobs.slice(0, 8);

  if (jobs.length === 0) {
    return <p className="glass mt-4 rounded-2xl p-6 text-sm text-muted-foreground">No visits yet. The first booking will show up here.</p>;
  }

  return (
    <ol className="relative mt-4 space-y-3 before:absolute before:top-2 before:bottom-2 before:left-[1.15rem] before:w-px before:bg-foreground/10">
      {visible.map((job, index) => (
        <motion.li
          key={job.id}
          initial={{ opacity: 0, x: 12 }}
          whileInView={{ opacity: 1, x: 0 }}
          viewport={{ once: true, margin: "-40px" }}
          transition={{ type: "spring", stiffness: 300, damping: 30, delay: Math.min(index, 6) * 0.04 }}
          className="relative pl-11"
        >
          <TimelineDot job={job} />
          <TimelineCard job={job} />
        </motion.li>
      ))}
      {jobs.length > 8 && (
        <li className="pl-11">
          <button type="button" onClick={() => setShowAll((value) => !value)} className="rounded-full px-3 py-1.5 text-sm font-medium text-muted-foreground hover:bg-foreground/[0.06] hover:text-foreground">
            {showAll ? "Show the latest 8" : `Show all ${jobs.length} visits`}
          </button>
        </li>
      )}
    </ol>
  );
}

// Literal class names: Tailwind only generates classes it can find written out in the source.
const DOT_COLOURS = { ember: "bg-ember", frost: "bg-frost", aqua: "bg-aqua", violet: "bg-violet" } as const;

function TimelineDot({ job }: { job: TimelineJob }) {
  const tone = job.priority === "Emergency" && job.status !== "Completed" ? "ember" : equipmentTone(job.equipmentKind as EquipmentKind | null);
  const open = job.status !== "Completed" && job.status !== "Cancelled";
  return (
    <span className={cn("absolute top-4 left-2.5 flex size-4 items-center justify-center rounded-full ring-4 ring-background", DOT_COLOURS[tone])}>
      {open && <span className={cn("absolute inset-0 rounded-full", DOT_COLOURS[tone])} style={{ animation: "pulse-ring 1.6s ease-out infinite" }} />}
    </span>
  );
}

function TimelineCard({ job }: { job: TimelineJob }) {
  const open = job.status !== "Completed" && job.status !== "Cancelled";
  const when = new Date(job.windowStart);
  return (
    <SpotlightCard className={cn("p-4", open && job.priority === "Emergency" && "glow-ember")}>
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <p className="flex flex-wrap gap-x-3 text-xs text-muted-foreground">
          <time dateTime={job.windowStart} className="font-medium text-foreground/80">
            {when.toLocaleDateString("en-CA", { month: "short", day: "numeric", year: "numeric" })}
          </time>
          <span>{fromNow(job.windowStart)}</span>
          <Link href={`/jobs/${job.id}`} className="tabular underline decoration-foreground/20 underline-offset-2 hover:text-foreground">
            {job.number}
          </Link>
        </p>
        <span className="flex gap-1.5">
          {job.priority === "Emergency" && <Badge className="bg-ember text-white">Emergency</Badge>}
          <Badge className={open ? "bg-frost-soft text-frost" : "bg-foreground/[0.06] text-foreground/70"}>{STATUS_LABELS[job.status]}</Badge>
        </span>
      </div>
      <p className="mt-1.5 font-medium">{job.summary}</p>
      {job.resolution && <p className="mt-1 text-sm text-muted-foreground">{job.resolution}</p>}
      <p className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
        {job.equipment && <span>{job.equipment}</span>}
        {job.technician && (
          <span className="inline-flex items-center gap-1">
            <UserRound className="size-3" /> {job.technician}
          </span>
        )}
        {job.firstTimeFix === true && (
          <span className="inline-flex items-center gap-1 text-success">
            <Check className="size-3" /> Fixed first visit
          </span>
        )}
        {job.firstTimeFix === false && (
          <span className="inline-flex items-center gap-1 text-warning">
            <CircleAlert className="size-3" /> Needed a return visit
          </span>
        )}
        {job.invoiceTotal !== null && job.invoiceTotal > 0 && <span className="tabular">${job.invoiceTotal.toFixed(2)}</span>}
      </p>
      {job.notes.map((note) => (
        <blockquote key={note.createdAt + note.author} className="mt-3 rounded-xl border-l-2 border-foreground/15 bg-foreground/[0.03] px-3 py-2 text-sm">
          <p>{note.body}</p>
          <footer className="mt-1 text-xs text-muted-foreground">{note.author}</footer>
        </blockquote>
      ))}
    </SpotlightCard>
  );
}

function Badge({ className, children }: { className: string; children: React.ReactNode }) {
  return <span className={cn("rounded-full px-2 py-0.5 text-xs font-semibold", className)}>{children}</span>;
}

function Loading() {
  return (
    <div className="mx-auto w-full max-w-6xl space-y-6" aria-label="Loading customer">
      <div className="h-16 w-80 animate-pulse rounded-2xl bg-foreground/[0.07]" />
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {[0, 1, 2, 3].map((i) => (
          <div key={i} className="glass h-24 animate-pulse rounded-2xl" />
        ))}
      </div>
    </div>
  );
}

function NotFound({ missing }: { missing: boolean }) {
  return (
    <div className="mx-auto flex w-full max-w-md flex-col items-center gap-3 py-24 text-center">
      <span className="flex size-12 items-center justify-center rounded-2xl bg-foreground/[0.06]">
        <UserRound className="size-5" />
      </span>
      <h1 className="type-title text-2xl">{missing ? "This customer isn't in your list" : "Couldn't load this customer"}</h1>
      <p className="text-sm text-muted-foreground">
        {missing ? "They may belong to another branch, or the link is out of date." : "Check the API is running, then try again."}
      </p>
      <Link href="/customers" className="mt-2 rounded-full bg-primary px-4 py-2 text-sm font-semibold text-primary-foreground">
        Back to customers
      </Link>
    </div>
  );
}
