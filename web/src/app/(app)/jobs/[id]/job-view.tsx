"use client";

import { AnimatePresence, motion } from "motion/react";
import { ArrowLeft, Clock3, KeyRound, MailOpen, MapPin, Phone, Send, ShieldAlert, UserRound } from "lucide-react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";
import { Reveal, RevealItem } from "@/components/motion/reveal";
import { SuggestionsPanel } from "@/components/dispatch/suggestions-panel";
import { JobStepper, StatusBadge } from "@/components/jobs/job-status";
import { SpotlightCard } from "@/components/motion/spotlight-card";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api";
import { equipmentLabels, type JobStatus } from "@/lib/customers";
import { statusActions, useAddNote, useChangeStatus, useCustomerUpdates, useJob, windowLabel, type JobDetail } from "@/lib/jobs";
import { useMe } from "@/lib/session";
import { cn } from "@/lib/utils";

const LICENCES: Record<string, string> = {
  None: "No licence needed",
  GasG2: "G2 gas licence",
  GasG1: "G1 gas licence",
  GasG3: "G3 gas licence",
  Refrigeration313A: "313A refrigeration licence",
  Plumber306A: "306A plumber's licence",
};

/** One job: where, when, who, what's wrong, what's been said, and the next step. */
export function JobView() {
  const { id } = useParams<{ id: string }>();
  const { data: job, error, isPending } = useJob(Number(id));
  const { data: me } = useMe();

  if (isPending) {
    return <div className="glass mx-auto h-64 w-full max-w-5xl animate-pulse rounded-3xl" aria-label="Loading job" />;
  }

  if (error || !job) {
    const missing = error instanceof ApiError && error.status === 404;
    return (
      <div className="mx-auto flex max-w-md flex-col items-center gap-3 py-24 text-center">
        <h1 className="type-title text-2xl">{missing ? "This job isn't in your list" : "Couldn't load this job"}</h1>
        <p className="text-sm text-muted-foreground">{missing ? "It may belong to another branch or another technician." : "Check the API is running, then try again."}</p>
        <Link href="/jobs" className="mt-2 rounded-full bg-primary px-4 py-2 text-sm font-semibold text-primary-foreground">
          Back to jobs
        </Link>
      </div>
    );
  }

  const emergency = job.priority === "Emergency" && job.status !== "Completed" && job.status !== "Cancelled";

  return (
    <Reveal className="mx-auto w-full max-w-5xl">
      <RevealItem>
        <Link href="/jobs" className="mb-6 inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-sm font-medium text-muted-foreground transition-colors hover:bg-foreground/[0.06] hover:text-foreground">
          <ArrowLeft className="size-4" /> Jobs
        </Link>
      </RevealItem>

      <RevealItem className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <span className="font-semibold text-foreground tabular">{job.number}</span>
            <StatusBadge status={job.status} />
            {emergency && <span className="rounded-full bg-ember px-2.5 py-0.5 text-xs font-bold text-white glow-ember">Emergency</span>}
          </p>
          <h1 className="type-display mt-3 max-w-3xl text-3xl md:text-4xl">{job.summary}</h1>
        </div>
        <Actions job={job} />
      </RevealItem>

      <RevealItem className="mt-8">
        <JobStepper status={job.status} />
      </RevealItem>

      {/* For a job still to be done, the next decision is who goes: put that first. */}
      {me?.can.canDispatch && (job.status === "Scheduled" || job.status === "Dispatched") && (
        <RevealItem className="mt-8">
          <SuggestionsPanel jobId={job.id} jobNumber={job.number} />
        </RevealItem>
      )}

      <div className="mt-10 grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.2fr)]">
        <div className="space-y-4">
          <RevealItem>
            <SpotlightCard className="space-y-4 p-5">
              <Fact icon={Clock3} label="When">
                {new Date(job.windowStart).toLocaleDateString("en-CA", { weekday: "long", month: "long", day: "numeric" })}, {windowLabel(job.windowStart, job.windowEnd)}
              </Fact>
              <Fact icon={MapPin} label="Where">
                {job.property.street}, {job.property.city}
              </Fact>
              <Fact icon={UserRound} label="Customer">
                <Link href={`/customers/${job.customer.id}`} className="font-medium underline decoration-foreground/25 underline-offset-4 hover:decoration-foreground">
                  {job.customer.name}
                </Link>{" "}
                <a href={`tel:${job.customer.phone}`} className="ml-1 inline-flex items-center gap-1 text-muted-foreground tabular hover:text-foreground">
                  <Phone className="size-3" /> {job.customer.phone}
                </a>
              </Fact>
              <Fact icon={ShieldAlert} label="Needs">
                {LICENCES[job.requiredCertification] ?? job.requiredCertification}
              </Fact>
              <Fact icon={UserRound} label="Technician">
                {job.technician ? (
                  <span className="inline-flex items-center gap-2">
                    <span className="size-2.5 rounded-full" style={{ background: job.technician.color }} />
                    {job.technician.name}
                  </span>
                ) : (
                  <span className="text-muted-foreground">Not assigned yet.</span>
                )}
              </Fact>
            </SpotlightCard>
          </RevealItem>

          {job.property.accessNotes && (
            <RevealItem>
              <p className="glass flex gap-3 rounded-2xl p-4 text-sm">
                <KeyRound className="mt-0.5 size-4 shrink-0 text-warning" aria-hidden />
                <span>
                  <span className="font-semibold">Getting in.</span> {job.property.accessNotes}
                </span>
              </p>
            </RevealItem>
          )}

          {job.equipment && (
            <RevealItem>
              <SpotlightCard className="p-5">
                <p className="text-sm text-muted-foreground">{equipmentLabels[job.equipment.kind]}</p>
                <p className="font-display text-xl font-semibold tracking-tight">
                  {job.equipment.brand} {job.equipment.model}
                </p>
                <p className="mt-1 text-xs text-muted-foreground tabular">
                  {job.equipment.serialNumber}, installed {new Date(job.equipment.installedOn).getFullYear()}
                  {job.equipment.warrantyExpiresOn && `, warranty to ${new Date(job.equipment.warrantyExpiresOn).getFullYear()}`}
                </p>
              </SpotlightCard>
            </RevealItem>
          )}

          {job.resolution && (
            <RevealItem>
              <div className="glass rounded-2xl p-5">
                <p className="text-sm font-semibold">What was done</p>
                <p className="mt-1 text-sm text-muted-foreground">{job.resolution}</p>
                {job.invoiceTotal !== null && <p className="mt-2 text-sm font-medium tabular">Invoiced ${job.invoiceTotal.toFixed(2)}</p>}
              </div>
            </RevealItem>
          )}
        </div>

        <RevealItem>
          <Notes job={job} />
        </RevealItem>
        <CustomerUpdates jobId={job.id} />
      </div>
    </Reveal>
  );
}

function Fact({ icon: Icon, label, children }: { icon: typeof Clock3; label: string; children: React.ReactNode }) {
  return (
    <div className="flex gap-3">
      <Icon className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden />
      <div className="min-w-0">
        <p className="text-xs text-muted-foreground">{label}</p>
        <p className="text-sm">{children}</p>
      </div>
    </div>
  );
}

/** The next steps this person may take. Completing opens a short form; the rest are one click. */
function Actions({ job }: { job: JobDetail }) {
  const change = useChangeStatus(job.id);
  const [completing, setCompleting] = useState(false);

  async function move(status: JobStatus, extra?: { resolution: string; invoiceTotal?: number; firstTimeFix: boolean }) {
    try {
      await change.mutateAsync({ status, ...extra });
      toast.success(statusActions[status]?.done ?? "Updated", { description: job.number });
      setCompleting(false);
    } catch (error) {
      toast.error(error instanceof ApiError ? error.message : "Couldn't update the job.");
    }
  }

  if (job.nextStatuses.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-col items-end gap-3">
      <div className="flex flex-wrap gap-2">
        {job.nextStatuses.map((status) =>
          status === "Completed" ? (
            <Button key={status} variant="thermal" size="lg" className="rounded-full px-4" onClick={() => setCompleting((v) => !v)}>
              {statusActions.Completed!.label}
            </Button>
          ) : (
            <Button
              key={status}
              size="lg"
              variant={status === "Cancelled" ? "ghost" : "default"}
              className={cn("rounded-full px-4", status === "Cancelled" && "glass text-destructive")}
              disabled={change.isPending}
              onClick={() => move(status)}
            >
              {statusActions[status]?.label ?? status}
            </Button>
          ),
        )}
      </div>
      <AnimatePresence>{completing && <CompleteForm busy={change.isPending} onSubmit={(values) => move("Completed", values)} />}</AnimatePresence>
    </div>
  );
}

function CompleteForm({ busy, onSubmit }: { busy: boolean; onSubmit: (values: { resolution: string; invoiceTotal?: number; firstTimeFix: boolean }) => void }) {
  const [resolution, setResolution] = useState("");
  const [invoice, setInvoice] = useState("");
  const [firstTimeFix, setFirstTimeFix] = useState(true);

  function submit(event: FormEvent) {
    event.preventDefault();
    onSubmit({ resolution, invoiceTotal: invoice ? Number(invoice) : undefined, firstTimeFix });
  }

  return (
    <motion.form
      onSubmit={submit}
      initial={{ opacity: 0, y: -8, scale: 0.98 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, y: -8, scale: 0.98 }}
      className="glass-strong w-full max-w-md space-y-3 rounded-2xl p-4"
    >
      <label className="block space-y-1.5">
        <span className="text-sm font-medium">What did you find and do?</span>
        <textarea autoFocus value={resolution} onChange={(e) => setResolution(e.target.value)} rows={3} className="glass w-full resize-none rounded-xl px-3 py-2 text-sm outline-none" placeholder="Ignitor cracked; replaced with IGN-80H and fitted bracket kit BK-80R." />
      </label>
      <div className="flex flex-wrap items-center gap-4">
        <label className="flex items-center gap-2 text-sm">
          <span>Invoice $</span>
          <input type="number" min="0" step="0.5" value={invoice} onChange={(e) => setInvoice(e.target.value)} className="glass h-9 w-28 rounded-lg px-2 text-sm tabular" />
        </label>
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={firstTimeFix} onChange={(e) => setFirstTimeFix(e.target.checked)} className="size-4 accent-[var(--success)]" />
          Fixed on this visit
        </label>
      </div>
      <Button type="submit" variant="thermal" className="w-full rounded-xl" disabled={busy || resolution.trim().length < 3}>
        Complete the job
      </Button>
    </motion.form>
  );
}

/**
 * Messages drafted for the customer by the notifications subscriber (for now: "your technician
 * is booked", when the job is assigned). Shown only once there's something to show, and always
 * as a draft: Truckroll has no way to send them.
 */
function CustomerUpdates({ jobId }: { jobId: number }) {
  const { data: updates } = useCustomerUpdates(jobId);
  if (!updates?.length) return null;

  return (
    <motion.section
      aria-labelledby="updates-heading"
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      className="glass rounded-3xl p-5"
    >
      <h2 id="updates-heading" className="type-title flex items-center gap-2 text-xl">
        <MailOpen className="size-5 text-muted-foreground" aria-hidden /> For the customer
      </h2>
      <p className="mt-1 text-sm text-muted-foreground">Drafted when the job changed. Not sent: copy it into your email or text when it&apos;s right.</p>
      <ol className="mt-4 space-y-3">
        {updates.map((update) => (
          <li key={update.id} className="rounded-2xl bg-foreground/[0.04] px-4 py-3 text-sm">
            <p className="font-semibold">{update.subject}</p>
            <p className="mt-1">{update.body}</p>
            <p className="mt-1 text-xs text-muted-foreground">
              Draft, {new Date(update.createdAt).toLocaleString("en-CA", { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" })}
            </p>
          </li>
        ))}
      </ol>
    </motion.section>
  );
}

function Notes({ job }: { job: JobDetail }) {
  const add = useAddNote(job.id);
  const [body, setBody] = useState("");

  async function submit(event: FormEvent) {
    event.preventDefault();
    try {
      await add.mutateAsync(body);
      setBody("");
    } catch {
      toast.error("Couldn't save the note. Try again.");
    }
  }

  return (
    <section aria-labelledby="notes-heading" className="glass rounded-3xl p-5">
      <h2 id="notes-heading" className="type-title text-xl">
        Notes
      </h2>
      <ol className="mt-4 space-y-3">
        <AnimatePresence initial={false}>
          {job.notes.map((note) => (
            <motion.li
              key={note.id}
              layout
              initial={{ opacity: 0, y: 10, scale: 0.98 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              className={cn("rounded-2xl px-4 py-3 text-sm", note.source === "Technician" ? "bg-frost-soft" : "bg-foreground/[0.04]")}
            >
              <p>{note.body}</p>
              <p className="mt-1 text-xs text-muted-foreground">
                {note.author}, {new Date(note.createdAt).toLocaleString("en-CA", { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" })}
              </p>
            </motion.li>
          ))}
        </AnimatePresence>
        {job.notes.length === 0 && <li className="text-sm text-muted-foreground">No notes yet. Anything the next person should know goes here.</li>}
      </ol>
      {job.canAddNotes && (
        <form onSubmit={submit} className="mt-4 flex items-end gap-2">
          <label className="flex-1">
            <span className="sr-only">Add a note</span>
            <textarea value={body} onChange={(e) => setBody(e.target.value)} rows={2} placeholder="Add a note" className="glass w-full resize-none rounded-2xl px-3 py-2.5 text-sm outline-none focus-visible:ring-3 focus-visible:ring-ring/50" />
          </label>
          <Button type="submit" size="icon-lg" className="size-11 rounded-full" aria-label="Add the note" disabled={add.isPending || !body.trim()}>
            <Send className="size-4" />
          </Button>
        </form>
      )}
    </section>
  );
}
