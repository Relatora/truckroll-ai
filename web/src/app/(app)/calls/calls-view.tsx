"use client";

import { motion } from "motion/react";
import { ClipboardPaste, Inbox, Phone } from "lucide-react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";
import { Segmented } from "@/components/controls/segmented";
import { PageHeader } from "@/components/shell/page-header";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import { ApiError } from "@/lib/api";
import { TABS, useCalls, usePasteCall, type CallListItem, type CallStatus } from "@/lib/calls";
import { useBranches } from "@/lib/customers";
import { useMe } from "@/lib/session";
import { cn } from "@/lib/utils";
import { CallReview } from "./call-review";

type Tab = keyof typeof TABS;

const STATUS: Record<CallStatus, { label: string; className: string }> = {
  New: { label: "New", className: "bg-frost-soft text-frost" },
  AwaitingReview: { label: "To review", className: "bg-violet-soft text-violet" },
  Approved: { label: "Approved", className: "bg-success-soft text-success" },
  Dismissed: { label: "Dismissed", className: "bg-foreground/[0.06] text-muted-foreground" },
};

const time = (iso: string) => new Date(iso).toLocaleTimeString("en-CA", { hour: "numeric", minute: "2-digit" });

/**
 * The calls inbox: this morning's calls on the left, the one being reviewed on the right. The
 * open call and the tab live in the URL (?call=&tab=) so a link opens the same review.
 */
export function CallsView() {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const { data: calls, isPending } = useCalls();
  const tab = (params.get("tab") as Tab | null) ?? "inbox";
  const selected = Number(params.get("call")) || null;

  const visible = (calls ?? []).filter((c) => (TABS[tab] as readonly CallStatus[]).includes(c.status));
  const counts = { inbox: 0, outbox: 0, closed: 0 } as Record<Tab, number>;
  (calls ?? []).forEach((c) => (Object.keys(TABS) as Tab[]).forEach((t) => (TABS[t] as readonly CallStatus[]).includes(c.status) && counts[t]++));

  function go(changes: { tab?: Tab; call?: number | null }) {
    const next = new URLSearchParams(params);
    if (changes.tab) next.set("tab", changes.tab);
    if (changes.call) next.set("call", String(changes.call));
    else if (changes.call === null) next.delete("call");
    router.replace(`${pathname}?${next}`, { scroll: false });
  }

  return (
    <div className="mx-auto w-full max-w-7xl">
      <PageHeader title="Calls" description="Each call becomes a proposed job and a drafted follow-up. Nothing is booked or filed until you approve it." actions={<PasteCall onAdded={(id) => go({ tab: "inbox", call: id })} />} />

      <div className="mb-5">
        <Segmented
          id="call-tabs"
          label="Which calls"
          value={tab}
          onChange={(value) => go({ tab: value as Tab, call: null })}
          options={[
            { value: "inbox", label: `Inbox ${counts.inbox}` },
            { value: "outbox", label: `Outbox ${counts.outbox}` },
            { value: "closed", label: `Dismissed ${counts.closed}` },
          ]}
        />
      </div>

      <div className="grid gap-5 lg:grid-cols-[22rem_minmax(0,1fr)]">
        <nav aria-label="Calls" className={cn("space-y-2", selected && "max-lg:hidden")}>
          {isPending ? (
            Array.from({ length: 4 }).map((_, i) => <div key={i} className="glass h-24 animate-pulse rounded-2xl" />)
          ) : visible.length === 0 ? (
            <Empty tab={tab} />
          ) : (
            visible.map((call, i) => <CallCard key={call.id} call={call} index={i} active={call.id === selected} onOpen={() => go({ call: call.id })} />)
          )}
        </nav>

        <section aria-label="Selected call" className={cn(!selected && "max-lg:hidden")}>
          {selected ? (
            <CallReview id={selected} onBack={() => go({ call: null })} />
          ) : (
            <div className="glass flex h-full min-h-72 flex-col items-center justify-center gap-2 rounded-3xl p-8 text-center text-muted-foreground">
              <Phone className="size-6" />
              <p>Pick a call to read it and review what Truckroll proposes.</p>
            </div>
          )}
        </section>
      </div>
    </div>
  );
}

function CallCard({ call, index, active, onOpen }: { call: CallListItem; index: number; active: boolean; onOpen: () => void }) {
  const status = STATUS[call.status];
  return (
    <motion.button
      type="button"
      onClick={onOpen}
      aria-current={active}
      initial={{ opacity: 0, x: -10 }}
      animate={{ opacity: 1, x: 0, transition: { delay: index * 0.04 } }}
      className={cn("glass block w-full rounded-2xl p-4 text-left transition-shadow hover:shadow-lift", active && "ring-2 ring-violet")}
    >
      <span className="flex items-center gap-2 text-xs text-muted-foreground">
        <span className="font-semibold text-foreground tabular">{time(call.receivedAt)}</span>
        {call.branchCode}
        <span className={cn("ml-auto rounded-full px-2 py-0.5 font-semibold", status.className)}>{status.label}</span>
        {call.priority === "Emergency" && <span className="rounded-full bg-ember px-2 py-0.5 font-bold text-white">Emergency</span>}
      </span>
      <span className="mt-1.5 block font-semibold">{call.caller ?? "Caller not identified yet"}</span>
      <span className="mt-0.5 line-clamp-2 block text-sm text-muted-foreground">{call.preview}</span>
    </motion.button>
  );
}

function Empty({ tab }: { tab: Tab }) {
  const text = {
    inbox: "No calls waiting. Paste a transcript to add one.",
    outbox: "No follow-ups yet. Approved calls file their follow-up here.",
    closed: "No dismissed calls.",
  }[tab];
  return (
    <div className="glass flex flex-col items-center gap-2 rounded-2xl p-8 text-center text-sm text-muted-foreground">
      <Inbox className="size-5" /> {text}
    </div>
  );
}

/** Paste a transcript from the phone system. Audio transcription is out of scope for v0.1. */
function PasteCall({ onAdded }: { onAdded: (id: number) => void }) {
  const { data: me } = useMe();
  const { data: branches } = useBranches();
  const paste = usePasteCall();
  const [open, setOpen] = useState(false);
  const [transcript, setTranscript] = useState("");
  const [branch, setBranch] = useState("");
  const [error, setError] = useState<string | null>(null);
  const owner = me?.role === "Owner";

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      const call = await paste.mutateAsync({ transcript, branchCode: owner ? branch || undefined : undefined });
      toast.success("Call added", { description: "Analyse it to get a proposed job and follow-up." });
      setTranscript("");
      setOpen(false);
      onAdded(call.id);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Couldn't add the call.");
    }
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button size="lg" className="sheen rounded-full px-4">
          <ClipboardPaste className="size-4" /> Paste a call
        </Button>
      </DialogTrigger>
      <DialogContent className="glass-strong rounded-3xl sm:max-w-xl">
        <DialogHeader>
          <DialogTitle className="type-title text-2xl">Paste a call</DialogTitle>
          <DialogDescription>The transcript from your phone system, one line per turn, like &quot;CSR: …&quot; and &quot;Caller: …&quot;.</DialogDescription>
        </DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          {owner && (
            <label className="block space-y-2">
              <span className="text-sm font-medium">Branch that took the call</span>
              <select value={branch} onChange={(e) => setBranch(e.target.value)} required className="glass h-11 w-full rounded-xl px-3 text-sm">
                <option value="">Pick a branch</option>
                {branches?.map((b) => (
                  <option key={b.code} value={b.code}>
                    {b.name}
                  </option>
                ))}
              </select>
            </label>
          )}
          <label className="block space-y-2">
            <span className="text-sm font-medium">Transcript</span>
            <textarea
              value={transcript}
              onChange={(e) => setTranscript(e.target.value)}
              rows={10}
              required
              minLength={40}
              className="glass w-full resize-y rounded-xl px-3 py-2.5 font-mono text-xs leading-relaxed outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
            />
          </label>
          {error && (
            <p role="alert" className="text-sm text-destructive">
              {error}
            </p>
          )}
          <Button type="submit" size="lg" className="w-full rounded-full" disabled={paste.isPending}>
            {paste.isPending ? "Adding…" : "Add to the inbox"}
          </Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}
