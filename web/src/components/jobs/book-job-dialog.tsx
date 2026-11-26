"use client";

import { useQuery } from "@tanstack/react-query";
import { AnimatePresence, motion } from "motion/react";
import { CalendarPlus, Search } from "lucide-react";
import { useRouter } from "next/navigation";
import { useDeferredValue, useState, type FormEvent } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import { api, ApiError } from "@/lib/api";
import { equipmentLabels, useCustomer, type JobPriority, type JobType } from "@/lib/customers";
import { useBookJob, WINDOWS } from "@/lib/jobs";
import { cn } from "@/lib/utils";

type Hit = { id: number; name: string; address: string | null; branchCode: string };

/**
 * Booking a job, from wherever you are. Opened from a customer's page it skips straight to the
 * form; opened from the jobs list it starts by finding the customer.
 *
 * The form never asks for a skill or licence: the server works those out from the equipment, so
 * a booking can't send a gas job to someone without a gas licence.
 */
export function BookJobDialog({ customerId, trigger }: { customerId?: number; trigger?: React.ReactNode }) {
  const [open, setOpen] = useState(false);
  const [chosen, setChosen] = useState<number | undefined>(customerId);

  return (
    <Dialog
      open={open}
      onOpenChange={(value) => {
        setOpen(value);
        if (!value) setChosen(customerId);
      }}
    >
      <DialogTrigger asChild>
        {trigger ?? (
          <Button variant="thermal" size="lg" className="rounded-full px-4">
            <CalendarPlus /> Book a job
          </Button>
        )}
      </DialogTrigger>
      <DialogContent className="glass-strong max-h-[90svh] overflow-y-auto rounded-3xl sm:max-w-lg">
        <DialogHeader>
          <DialogTitle className="type-title text-2xl">Book a job</DialogTitle>
          <DialogDescription>Pick the home and the equipment. Truckroll works out which licence the job needs.</DialogDescription>
        </DialogHeader>
        <AnimatePresence mode="wait" initial={false}>
          {chosen === undefined ? (
            <motion.div key="find" initial={{ opacity: 0, x: -12 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: -12 }}>
              <FindCustomer onPick={setChosen} />
            </motion.div>
          ) : (
            <motion.div key="form" initial={{ opacity: 0, x: 12 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: 12 }}>
              <BookingForm customerId={chosen} onBack={customerId ? undefined : () => setChosen(undefined)} onDone={() => setOpen(false)} />
            </motion.div>
          )}
        </AnimatePresence>
      </DialogContent>
    </Dialog>
  );
}

function FindCustomer({ onPick }: { onPick: (id: number) => void }) {
  const [search, setSearch] = useState("");
  const term = useDeferredValue(search.trim());
  const { data: hits, isFetching } = useQuery({
    queryKey: ["customers", "search", term],
    queryFn: () => api<Hit[]>(`/customers/search?q=${encodeURIComponent(term)}`),
    enabled: term.length >= 2,
  });

  return (
    <div className="space-y-3">
      <label className="glass flex h-11 items-center gap-2.5 rounded-full px-4 focus-within:ring-3 focus-within:ring-ring/50">
        <Search className="size-4 text-muted-foreground" aria-hidden />
        <span className="sr-only">Find the customer</span>
        <input autoFocus value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Customer's name, phone or street" className="h-full flex-1 bg-transparent text-sm outline-none" />
      </label>
      <ul className="max-h-72 space-y-1 overflow-y-auto">
        {(hits ?? []).map((hit) => (
          <li key={hit.id}>
            <button type="button" onClick={() => onPick(hit.id)} className="flex w-full items-center justify-between gap-3 rounded-xl px-3 py-2.5 text-left transition-colors hover:bg-foreground/[0.05] focus-visible:bg-foreground/[0.07] focus-visible:outline-none">
              <span className="min-w-0">
                <span className="block font-medium">{hit.name}</span>
                <span className="block truncate text-sm text-muted-foreground">{hit.address}</span>
              </span>
              <span className="rounded-full bg-foreground/[0.06] px-2 py-0.5 text-xs font-semibold">{hit.branchCode}</span>
            </button>
          </li>
        ))}
        {term.length >= 2 && !isFetching && hits?.length === 0 && <li className="px-3 py-4 text-sm text-muted-foreground">Nobody matches that. Try a street or part of a phone number.</li>}
      </ul>
    </div>
  );
}

const TYPES: { value: JobType; label: string }[] = [
  { value: "Repair", label: "Repair" },
  { value: "Maintenance", label: "Tune-up" },
  { value: "Installation", label: "Installation" },
  { value: "Estimate", label: "Estimate" },
];

const PRIORITIES: { value: JobPriority; label: string; hint: string }[] = [
  { value: "Normal", label: "Normal", hint: "Next available slot" },
  { value: "Urgent", label: "Urgent", hint: "Within 2 business days" },
  { value: "Emergency", label: "Emergency", hint: "Same day, see P-101" },
];

function BookingForm({ customerId, onBack, onDone }: { customerId: number; onBack?: () => void; onDone: () => void }) {
  const router = useRouter();
  const { data: customer } = useCustomer(customerId);
  const book = useBookJob();

  const home = customer?.properties[0];
  const [equipmentId, setEquipmentId] = useState<number | null>(null);
  const [type, setType] = useState<JobType>("Repair");
  const [priority, setPriority] = useState<JobPriority>("Normal");
  const [date, setDate] = useState(() => new Date().toLocaleDateString("en-CA"));
  const [hour, setHour] = useState(10);
  const [summary, setSummary] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!home) return;
    setErrors({});
    try {
      const job = await book.mutateAsync({ propertyId: home.id, equipmentId, type, priority, date, windowStartHour: hour, summary });
      toast.success(`Booked ${job.number}`, { description: `${customer!.firstName} ${customer!.lastName}, ${WINDOWS.find((w) => w.hour === hour)!.label}` });
      onDone();
      router.push(`/jobs/${job.id}`);
    } catch (error) {
      if (error instanceof ApiError && error.status === 400) {
        setErrors({ form: error.message });
      } else {
        setErrors({ form: "Couldn't book the job. Check the API is running and try again." });
      }
    }
  }

  if (!customer || !home) {
    return <div className="h-64 animate-pulse rounded-2xl bg-foreground/[0.05]" />;
  }

  return (
    <form onSubmit={submit} className="space-y-5">
      <div className="flex items-center justify-between gap-3 rounded-2xl bg-foreground/[0.04] px-4 py-3">
        <span>
          <span className="block font-semibold">
            {customer.firstName} {customer.lastName}
          </span>
          <span className="block text-sm text-muted-foreground">
            {home.street}, {home.city}
          </span>
        </span>
        {onBack && (
          <button type="button" onClick={onBack} className="text-sm font-medium text-muted-foreground hover:text-foreground">
            Change
          </button>
        )}
      </div>

      <Field label="What's it about?">
        <div className="grid grid-cols-2 gap-2">
          {home.equipment.map((item) => (
            <Choice key={item.id} active={equipmentId === item.id} onClick={() => setEquipmentId(item.id)}>
              <span className="block text-xs text-muted-foreground">{equipmentLabels[item.kind]}</span>
              <span className="block truncate font-medium">{item.model}</span>
            </Choice>
          ))}
          <Choice active={equipmentId === null} onClick={() => setEquipmentId(null)}>
            <span className="block text-xs text-muted-foreground">Something else</span>
            <span className="block font-medium">No equipment yet</span>
          </Choice>
        </div>
      </Field>

      <div className="grid gap-5 sm:grid-cols-2">
        <Field label="Kind of visit">
          <select value={type} onChange={(e) => setType(e.target.value as JobType)} className="glass h-11 w-full rounded-xl px-3 text-sm">
            {TYPES.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </select>
        </Field>
        <Field label="How soon?">
          <select value={priority} onChange={(e) => setPriority(e.target.value as JobPriority)} className="glass h-11 w-full rounded-xl px-3 text-sm">
            {PRIORITIES.map((p) => (
              <option key={p.value} value={p.value}>
                {p.label}: {p.hint}
              </option>
            ))}
          </select>
        </Field>
      </div>

      <div className="grid gap-5 sm:grid-cols-2">
        <Field label="Day">
          <input type="date" value={date} onChange={(e) => setDate(e.target.value)} className="glass h-11 w-full rounded-xl px-3 text-sm" required />
        </Field>
        <Field label="Arrival window">
          <select value={hour} onChange={(e) => setHour(Number(e.target.value))} className="glass h-11 w-full rounded-xl px-3 text-sm">
            {WINDOWS.map((w) => (
              <option key={w.hour} value={w.hour}>
                {w.label}
              </option>
            ))}
          </select>
        </Field>
      </div>

      <Field label="What did the customer say?">
        <textarea
          value={summary}
          onChange={(e) => setSummary(e.target.value)}
          rows={3}
          placeholder="Furnace runs but blows cold air, started last night."
          className="glass w-full resize-none rounded-xl px-3 py-2.5 text-sm outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
        />
      </Field>

      {errors.form && (
        <p role="alert" className="text-sm text-destructive">
          {errors.form}
        </p>
      )}

      <Button type="submit" variant="thermal" size="xl" className="w-full" disabled={book.isPending || summary.trim().length < 5}>
        {book.isPending ? "Booking…" : "Book the job"}
      </Button>
    </form>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block space-y-2">
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
      className={cn(
        "rounded-xl border px-3 py-2.5 text-left text-sm transition-[transform,border-color,box-shadow] duration-200 ease-[var(--ease-spring)] hover:-translate-y-0.5 active:scale-[0.98]",
        active ? "border-frost bg-frost-soft shadow-float" : "border-foreground/10 bg-card/50",
      )}
    >
      {children}
    </button>
  );
}
