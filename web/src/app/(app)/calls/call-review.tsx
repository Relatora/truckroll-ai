"use client";

import { motion } from "motion/react";
import { AlertTriangle, ArrowLeft, Check, Copy, Info, MailCheck, Sparkles } from "lucide-react";
import Link from "next/link";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api";
import { useAnalyse, useApprove, useCall, useDismiss, type CallDetail, type CallProposal, type CustomerSource } from "@/lib/calls";
import { equipmentLabels, type JobPriority, type JobType } from "@/lib/customers";
import { cn } from "@/lib/utils";

const TYPES: { value: JobType; label: string }[] = [
  { value: "Repair", label: "Repair" },
  { value: "Maintenance", label: "Tune-up" },
  { value: "Installation", label: "Installation" },
  { value: "Estimate", label: "Estimate" },
];
const PRIORITIES: JobPriority[] = ["Normal", "Urgent", "Emergency"];
const WINDOWS = [
  { hour: 8, label: "8 to 10 am" },
  { hour: 10, label: "10 am to noon" },
  { hour: 12, label: "Noon to 2 pm" },
  { hour: 14, label: "2 to 4 pm" },
];
const SOURCES: { value: CustomerSource; label: string }[] = [
  { value: "Referral", label: "A referral" },
  { value: "GoogleSearch", label: "Google search" },
  { value: "GoogleReviews", label: "Google reviews" },
  { value: "Facebook", label: "Facebook" },
  { value: "YardSign", label: "A yard sign" },
  { value: "TruckWrap", label: "One of our trucks" },
  { value: "HomeShow", label: "A home show" },
];

/** One call: the conversation, then whatever stage it's at (analyse, review, filed, dismissed). */
export function CallReview({ id, onBack }: { id: number; onBack: () => void }) {
  const { data: call, isPending, error } = useCall(id);

  if (isPending) return <div className="glass h-96 animate-pulse rounded-3xl" aria-label="Loading the call" />;
  if (error || !call) return <p className="glass rounded-3xl p-8 text-sm text-muted-foreground">This call isn&apos;t in your inbox. It may belong to another branch.</p>;

  return (
    <motion.div key={call.id} initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} className="space-y-5">
      <button type="button" onClick={onBack} className="inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-sm font-medium text-muted-foreground hover:bg-foreground/[0.06] lg:hidden">
        <ArrowLeft className="size-4" /> All calls
      </button>

      <div>
        <p className="text-sm text-muted-foreground">
          {call.branch.name}, {new Date(call.receivedAt).toLocaleString("en-CA", { weekday: "long", hour: "numeric", minute: "2-digit" })}
        </p>
        <h2 className="type-title mt-1 text-3xl">{call.proposal?.callerName ?? "A call to review"}</h2>
        {call.summary && <p className="mt-2 max-w-2xl text-muted-foreground">{call.summary}</p>}
      </div>

      {call.status === "New" && <AnalysePrompt call={call} />}
      {call.status === "AwaitingReview" && call.proposal && <ReviewForm key={`${call.id}-${call.proposal.attempts}-${call.summary}`} call={call} proposal={call.proposal} />}
      {call.status === "Approved" && <Filed call={call} />}
      {call.status === "Dismissed" && (
        <p className="glass rounded-2xl p-4 text-sm text-muted-foreground">
          Dismissed by {call.reviewedBy}. Nothing was booked or filed.
        </p>
      )}

      <Transcript text={call.transcript} />
    </motion.div>
  );
}

function AnalysePrompt({ call }: { call: CallDetail }) {
  const analyse = useAnalyse(call.id);
  const [problems, setProblems] = useState<string[] | null>(null);

  async function run() {
    setProblems(null);
    try {
      await analyse.mutateAsync();
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : "Couldn't analyse the call.");
      if (e instanceof ApiError && e.status === 422) setProblems(["The model's answer wasn't usable twice in a row. Try again, or book the job from the customer's page."]);
    }
  }

  return (
    <div className="ai-drafted glass-strong rounded-3xl p-6" data-generating={analyse.isPending}>
      <p className="font-semibold">Let Truckroll read this call</p>
      <p className="mt-1 text-sm text-muted-foreground">
        It proposes the customer, a job and a follow-up email for you to check. Phone numbers and emails are hidden from the model and put back for you.
      </p>
      <Button size="lg" className="sheen mt-4 rounded-full px-5" onClick={run} disabled={analyse.isPending}>
        <Sparkles className="size-4" /> {analyse.isPending ? <span className="shimmer-text">Reading the call</span> : "Analyse the call"}
      </Button>
      {problems && <p className="mt-3 text-sm text-destructive">{problems[0]}</p>}
    </div>
  );
}

/**
 * The proposal, every part editable. The model drafted it; code matched the customer and
 * applied the safety rule; the CSR decides. Approve books the job and files the follow-up.
 */
function ReviewForm({ call, proposal }: { call: CallDetail; proposal: CallProposal }) {
  const approve = useApprove(call.id);
  const dismiss = useDismiss(call.id);
  const analyse = useAnalyse(call.id);

  async function reread() {
    try {
      await analyse.mutateAsync();
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : "Couldn't analyse the call again.");
    }
  }
  const options = proposal.candidates;
  const [home, setHome] = useState<string>(proposal.match ? String(proposal.match.propertyId) : options.length ? String(options[0].propertyId) : "new");
  const [first, ...rest] = proposal.callerName.split(" ");
  const [fresh, setFresh] = useState({ firstName: first ?? "", lastName: rest.join(" "), street: proposal.street ?? "", email: proposal.callerEmail ?? "", source: "Referral" as CustomerSource });
  const chosen = options.find((o) => String(o.propertyId) === home);
  const [equipmentId, setEquipmentId] = useState<number | null>(proposal.equipmentId);
  const [type, setType] = useState(proposal.type);
  const [priority, setPriority] = useState(proposal.priority);
  const [date, setDate] = useState(proposal.date ?? tomorrow());
  const [hour, setHour] = useState(proposal.windowStartHour ?? 10);
  const [summary, setSummary] = useState(proposal.symptoms);
  const [subject, setSubject] = useState(call.followUpSubject ?? "");
  const [body, setBody] = useState(call.followUpBody ?? "");

  function pickHome(value: string) {
    setHome(value);
    // Keep the unit the call was about, if this home has one of that kind.
    const option = options.find((o) => String(o.propertyId) === value);
    setEquipmentId(option?.equipment.find((e) => e.kind === proposal.equipment)?.id ?? null);
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    try {
      const done = await approve.mutateAsync({
        ...(home === "new" ? { newCustomer: { ...fresh, email: fresh.email || undefined } } : { propertyId: Number(home) }),
        equipmentId: home === "new" ? null : equipmentId,
        type,
        priority,
        date,
        windowStartHour: hour,
        summary,
        followUpSubject: subject,
        followUpBody: body,
      });
      toast.success(`Booked ${done.createdJob?.number}`, { description: "The follow-up is in the outbox. Nothing was emailed." });
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : "Couldn't approve the call.");
    }
  }

  async function close() {
    try {
      await dismiss.mutateAsync();
      toast("Call dismissed", { description: "Nothing was booked or filed." });
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : "Couldn't dismiss the call.");
    }
  }

  return (
    <form onSubmit={submit} className="ai-drafted glass-strong space-y-6 rounded-3xl p-5 md:p-6">
      <p className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
        <Sparkles className="size-3.5 text-violet" /> Drafted by {proposal.model ?? "the model"}
        {proposal.attempts > 1 && `, on its second try`}. Check every part before approving.
        <button type="button" onClick={reread} disabled={analyse.isPending} className="ml-auto rounded-full px-2 py-1 font-medium underline underline-offset-4 hover:text-foreground">
          {analyse.isPending ? <span className="shimmer-text">Reading again</span> : "Analyse again"}
        </button>
      </p>

      {proposal.notes.length > 0 && (
        <ul className="space-y-1.5">
          {proposal.notes.map((note) => {
            const warn = /Emergency|Safety/.test(note);
            return (
              <li key={note} className={cn("flex gap-2 rounded-xl px-3 py-2 text-sm", warn ? "bg-ember-soft font-medium" : "bg-foreground/[0.04] text-muted-foreground")}>
                {warn ? <AlertTriangle className="mt-0.5 size-4 shrink-0 text-ember" /> : <Info className="mt-0.5 size-4 shrink-0" />}
                {note}
              </li>
            );
          })}
        </ul>
      )}

      <fieldset className="space-y-3">
        <legend className="mb-2 font-semibold">Customer</legend>
        <div className="grid gap-2 sm:grid-cols-2">
          {options.map((o) => (
            <Choice key={o.propertyId} active={home === String(o.propertyId)} onClick={() => pickHome(String(o.propertyId))}>
              <span className="block font-medium">{o.name}</span>
              <span className="block text-xs text-muted-foreground">{o.address}</span>
            </Choice>
          ))}
          <Choice active={home === "new"} onClick={() => pickHome("new")}>
            <span className="block font-medium">New customer</span>
            <span className="block text-xs text-muted-foreground">Add {proposal.callerName} to Truckroll</span>
          </Choice>
        </div>

        {home === "new" && (
          <div className="grid gap-3 rounded-2xl bg-foreground/[0.03] p-3 sm:grid-cols-2">
            <Field label="First name">
              <input required value={fresh.firstName} onChange={(e) => setFresh({ ...fresh, firstName: e.target.value })} className="field" />
            </Field>
            <Field label="Last name">
              <input required value={fresh.lastName} onChange={(e) => setFresh({ ...fresh, lastName: e.target.value })} className="field" />
            </Field>
            <Field label="Street address">
              <input required value={fresh.street} onChange={(e) => setFresh({ ...fresh, street: e.target.value })} className="field" placeholder="41 Thornbury Ave" />
            </Field>
            <Field label="Email">
              <input type="email" value={fresh.email} onChange={(e) => setFresh({ ...fresh, email: e.target.value })} className="field" />
            </Field>
            <Field label="How they found us">
              <select value={fresh.source} onChange={(e) => setFresh({ ...fresh, source: e.target.value as CustomerSource })} className="field">
                {SOURCES.map((s) => (
                  <option key={s.value} value={s.value}>
                    {s.label}
                  </option>
                ))}
              </select>
            </Field>
          </div>
        )}

        {chosen && chosen.equipment.length > 0 && (
          <Field label="Equipment">
            <select value={equipmentId ?? ""} onChange={(e) => setEquipmentId(e.target.value ? Number(e.target.value) : null)} className="field">
              <option value="">No specific unit</option>
              {chosen.equipment.map((e) => (
                <option key={e.id} value={e.id}>
                  {equipmentLabels[e.kind]}: {e.label}
                </option>
              ))}
            </select>
          </Field>
        )}
      </fieldset>

      <fieldset className="space-y-3">
        <legend className="mb-2 font-semibold">Job</legend>
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Kind of visit">
            <select value={type} onChange={(e) => setType(e.target.value as JobType)} className="field">
              {TYPES.map((t) => (
                <option key={t.value} value={t.value}>
                  {t.label}
                </option>
              ))}
            </select>
          </Field>
          <Field label="How soon?">
            <select value={priority} onChange={(e) => setPriority(e.target.value as JobPriority)} className={cn("field", priority === "Emergency" && "font-semibold text-ember")}>
              {PRIORITIES.map((p) => (
                <option key={p} value={p}>
                  {p}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Day">
            <input type="date" required value={date} onChange={(e) => setDate(e.target.value)} className="field" />
          </Field>
          <Field label="Arrival window">
            <select value={hour} onChange={(e) => setHour(Number(e.target.value))} className="field">
              {WINDOWS.map((w) => (
                <option key={w.hour} value={w.hour}>
                  {w.label}
                </option>
              ))}
            </select>
          </Field>
        </div>
        {!proposal.date && <p className="text-xs text-muted-foreground">No day was agreed on the call: tomorrow is filled in. Check it with the customer.</p>}
        <Field label="What the customer said">
          <textarea required rows={2} value={summary} onChange={(e) => setSummary(e.target.value)} className="field resize-none py-2.5" />
        </Field>
      </fieldset>

      <fieldset className="space-y-3">
        <legend className="mb-2 font-semibold">Follow-up email</legend>
        <Field label="Subject">
          <input required value={subject} onChange={(e) => setSubject(e.target.value)} className="field" />
        </Field>
        <Field label="Message">
          <textarea required rows={7} value={body} onChange={(e) => setBody(e.target.value)} className="field resize-y py-2.5 leading-relaxed" />
        </Field>
        <p className="text-xs text-muted-foreground">Approving files this in the outbox for you to send from your own email. Truckroll never sends it.</p>
      </fieldset>

      <div className="flex flex-wrap items-center gap-3 border-t border-foreground/[0.07] pt-5">
        <Button type="submit" size="lg" className="sheen rounded-full px-5" disabled={approve.isPending}>
          <Check className="size-4" /> {approve.isPending ? "Booking…" : "Approve: book the job and file the follow-up"}
        </Button>
        <Button type="button" variant="ghost" size="lg" className="glass rounded-full px-4" onClick={close} disabled={dismiss.isPending}>
          Dismiss the call
        </Button>
      </div>
    </form>
  );
}

/** Approved: the job that was booked and the follow-up waiting to be sent by a person. */
function Filed({ call }: { call: CallDetail }) {
  async function copy() {
    await navigator.clipboard.writeText(`${call.followUpSubject}\n\n${call.followUpBody}`);
    toast.success("Follow-up copied", { description: "Paste it into your email." });
  }

  return (
    <div className="space-y-4">
      <p className="flex flex-wrap items-center gap-2 rounded-2xl bg-success-soft p-4 text-sm">
        <MailCheck className="size-4 text-success" />
        Approved by {call.reviewedBy}.
        {call.createdJob && (
          <Link href={`/jobs/${call.createdJob.id}`} className="font-semibold underline underline-offset-4">
            Open {call.createdJob.number}
          </Link>
        )}
      </p>
      <div className="glass rounded-3xl p-5">
        <div className="flex items-start justify-between gap-3">
          <div>
            <p className="text-xs text-muted-foreground">Outbox: not sent</p>
            <p className="font-semibold">{call.followUpSubject}</p>
          </div>
          <Button variant="ghost" className="glass rounded-full" onClick={copy}>
            <Copy className="size-4" /> Copy
          </Button>
        </div>
        <p className="mt-3 text-sm leading-relaxed whitespace-pre-line">{call.followUpBody}</p>
      </div>
    </div>
  );
}

/** The conversation, with the caller's lines set apart from the office's. */
function Transcript({ text }: { text: string }) {
  const lines = text.split("\n").filter((l) => l.trim());
  return (
    <details className="glass group rounded-3xl p-5" open>
      <summary className="cursor-pointer list-none font-semibold">The call</summary>
      <ol className="mt-3 max-h-[28rem] space-y-2 overflow-y-auto pr-1 text-sm">
        {lines.map((line, i) => {
          const [speaker, ...words] = line.split(":");
          const caller = /caller/i.test(speaker);
          const known = words.length > 0 && speaker.length < 20;
          return (
            <li key={i} className={cn("rounded-xl px-3 py-2", caller ? "bg-frost-soft/60" : "bg-foreground/[0.03]")}>
              {known && <span className={cn("mr-1.5 font-semibold", caller ? "text-frost" : "text-muted-foreground")}>{speaker}</span>}
              {known ? words.join(":").trim() : line}
            </li>
          );
        })}
      </ol>
    </details>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block space-y-1.5">
      <span className="text-sm font-medium">{label}</span>
      {children}
    </label>
  );
}

function Choice({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      className={cn("rounded-xl border px-3 py-2.5 text-left text-sm transition-[border-color,box-shadow]", active ? "border-violet bg-violet-soft/50 shadow-float" : "border-foreground/10 bg-card/50 hover:border-foreground/25")}
    >
      {children}
    </button>
  );
}

function tomorrow() {
  const d = new Date();
  d.setDate(d.getDate() + 1);
  return d.toLocaleDateString("en-CA");
}
