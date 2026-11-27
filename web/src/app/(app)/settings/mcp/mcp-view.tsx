"use client";

import { AnimatePresence, motion } from "motion/react";
import { Check, Copy, KeyRound, ShieldCheck, Sparkles, X } from "lucide-react";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shell/page-header";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ApiError } from "@/lib/api";
import { useHydrated } from "@/lib/use-hydrated";
import { claudeDesktopConfig, useCreateToken, useRevokeToken, useTokens, type CreatedToken, type TokenItem } from "@/lib/tokens";
import { cn } from "@/lib/utils";

const TOOLS: { name: string; does: string }[] = [
  { name: "search_customers", does: "Find a customer by name, street or phone." },
  { name: "get_customer_history", does: "Their homes, equipment, warranties and recent jobs with notes." },
  { name: "list_jobs", does: "Today's (or upcoming, or past) jobs you can see." },
  { name: "get_schedule", does: "A branch's dispatch board for a day." },
  { name: "suggest_technicians", does: "Who should go to a waiting job, ranked by Truckroll's rules." },
  { name: "ask_knowledge_base", does: "Cited passages from manuals, policies and job history." },
  { name: "create_draft_note", does: "Add a note to a job, labelled as an AI draft. The only change it can make." },
];

const day = (iso: string) => new Date(iso).toLocaleDateString("en-CA", { month: "short", day: "numeric", year: "numeric" });

/**
 * MCP access: personal tokens that let Claude Desktop (or another MCP client) use Truckroll as
 * you, with your access and nothing more.
 */
export function McpView() {
  const { data: tokens, isPending } = useTokens();
  const [created, setCreated] = useState<CreatedToken | null>(null);
  const live = tokens?.filter((t) => t.live) ?? [];
  const past = tokens?.filter((t) => !t.live) ?? [];

  return (
    <div className="mx-auto w-full max-w-4xl space-y-8">
      <PageHeader title="MCP access" description="Let Claude Desktop use Truckroll as you: it sees what you can see, and the only change it can make is a draft note." />

      <CreateToken onCreated={setCreated} />

      <AnimatePresence>{created && <ShowOnce created={created} onDone={() => setCreated(null)} />}</AnimatePresence>

      <section aria-labelledby="your-tokens" className="space-y-3">
        <h2 id="your-tokens" className="type-title text-2xl">
          Your tokens
        </h2>
        {isPending ? (
          <div className="glass h-32 animate-pulse rounded-3xl" />
        ) : live.length === 0 ? (
          <p className="glass rounded-2xl p-5 text-sm text-muted-foreground">No live tokens. Create one above for each device you use.</p>
        ) : (
          <ul className="space-y-2">
            {live.map((t) => (
              <TokenRow key={t.id} token={t} />
            ))}
          </ul>
        )}
        {past.length > 0 && (
          <details className="text-sm">
            <summary className="cursor-pointer rounded-full px-2 py-1 font-medium text-muted-foreground hover:text-foreground">Revoked and expired ({past.length})</summary>
            <ul className="mt-2 space-y-1 pl-2 text-muted-foreground">
              {past.map((t) => (
                <li key={t.id}>
                  <span className="font-medium text-foreground">{t.name}</span> ({t.prefix}…), {t.revokedAt ? `revoked ${day(t.revokedAt)}` : `expired ${day(t.expiresAt)}`}
                </li>
              ))}
            </ul>
          </details>
        )}
      </section>

      <div className="grid gap-5 md:grid-cols-2">
        <section aria-labelledby="what-it-can-do" className="glass rounded-3xl p-5">
          <h2 id="what-it-can-do" className="flex items-center gap-2 font-semibold">
            <Sparkles className="size-4 text-violet" /> What Claude can do
          </h2>
          <ul className="mt-3 space-y-2 text-sm">
            {TOOLS.map((t) => (
              <li key={t.name}>
                <code className="rounded bg-foreground/[0.06] px-1.5 py-0.5 text-xs">{t.name}</code>
                <span className="ml-1.5 text-muted-foreground">{t.does}</span>
              </li>
            ))}
          </ul>
        </section>
        <section aria-labelledby="how-it-stays-safe" className="glass rounded-3xl p-5">
          <h2 id="how-it-stays-safe" className="flex items-center gap-2 font-semibold">
            <ShieldCheck className="size-4 text-success" /> How it stays safe
          </h2>
          <ul className="mt-3 list-disc space-y-2 pl-5 text-sm text-muted-foreground marker:text-foreground/30">
            <li>A token acts as you, through the same access rules as this app. A technician&apos;s token sees a technician&apos;s jobs.</li>
            <li>Customers&apos; phone numbers and emails are hidden from Claude, as from every model in Truckroll.</li>
            <li>Truckroll stores only a fingerprint of each token, never the token itself.</li>
            <li>Revoking takes effect on the very next request. Tokens expire after 90 days.</li>
          </ul>
        </section>
      </div>
    </div>
  );
}

function CreateToken({ onCreated }: { onCreated: (created: CreatedToken) => void }) {
  const create = useCreateToken();
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      onCreated(await create.mutateAsync(name));
      setName("");
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Couldn't create the token.");
    }
  }

  return (
    <form onSubmit={submit} className="glass-strong rounded-3xl p-5">
      <label htmlFor="token-name" className="font-semibold">
        New token
      </label>
      <p className="mt-0.5 text-sm text-muted-foreground">Name it after the device you&apos;ll use it on, so you know which to revoke if you lose it.</p>
      <div className="mt-3 flex flex-wrap gap-2">
        <input id="token-name" required maxLength={60} value={name} onChange={(e) => setName(e.target.value)} placeholder="Work laptop" className="field max-w-sm flex-1" />
        <Button type="submit" size="lg" className="sheen rounded-full px-5" disabled={create.isPending}>
          <KeyRound className="size-4" /> {create.isPending ? "Creating…" : "Create token"}
        </Button>
      </div>
      {error && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          {error}
        </p>
      )}
    </form>
  );
}

/** The one time the token exists on screen, with the Claude Desktop config already filled in. */
function ShowOnce({ created, onDone }: { created: CreatedToken; onDone: () => void }) {
  const hydrated = useHydrated();
  const origin = hydrated ? window.location.origin : "http://localhost:3000";
  const config = claudeDesktopConfig(origin, created.token);

  return (
    <motion.section
      initial={{ opacity: 0, y: 12, scale: 0.98 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, y: -8 }}
      aria-live="polite"
      className="ai-drafted glass-strong space-y-4 rounded-3xl p-5"
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="font-semibold">&ldquo;{created.item.name}&rdquo; is ready. Copy it now.</p>
          <p className="text-sm text-muted-foreground">Truckroll keeps only a fingerprint of it, so this is the only time you&apos;ll see it.</p>
        </div>
        <button type="button" onClick={onDone} aria-label="I've copied it, hide the token" className="rounded-full p-1.5 text-muted-foreground hover:bg-foreground/[0.06]">
          <X className="size-4" />
        </button>
      </div>
      <CopyBlock label="Token" text={created.token} />
      <div>
        <p className="mb-1.5 text-sm font-medium">Claude Desktop config</p>
        <p className="mb-2 text-xs text-muted-foreground">
          In Claude Desktop, open Settings, then Developer, then Edit config, and paste this in (merge it with any servers already there). Needs Node.js for the
          mcp-remote bridge. Restart Claude Desktop afterwards.
        </p>
        <CopyBlock label="Config" text={config} multiline />
      </div>
    </motion.section>
  );
}

function CopyBlock({ label, text, multiline }: { label: string; text: string; multiline?: boolean }) {
  const [copied, setCopied] = useState(false);
  async function copy() {
    await navigator.clipboard.writeText(text);
    setCopied(true);
    toast.success(`${label} copied`);
    setTimeout(() => setCopied(false), 1600);
  }
  return (
    <div className="relative">
      <pre className={cn("overflow-x-auto rounded-xl bg-foreground/[0.05] p-3 pr-24 font-mono text-xs", !multiline && "whitespace-nowrap")}>{text}</pre>
      <Button type="button" size="sm" variant="ghost" className="glass absolute top-2 right-2 rounded-full" onClick={copy} aria-label={`Copy the ${label.toLowerCase()}`}>
        {copied ? <Check className="size-3.5" /> : <Copy className="size-3.5" />} {copied ? "Copied" : "Copy"}
      </Button>
    </div>
  );
}

function TokenRow({ token }: { token: TokenItem }) {
  const revoke = useRevokeToken();
  const [confirming, setConfirming] = useState(false);

  async function confirm() {
    try {
      await revoke.mutateAsync(token.id);
      toast.success(`Revoked "${token.name}"`, { description: "Anything using it has stopped working." });
      setConfirming(false);
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : "Couldn't revoke the token.");
    }
  }

  return (
    <li className="glass flex flex-wrap items-center gap-x-4 gap-y-2 rounded-2xl p-4">
      <KeyRound className="size-4 text-muted-foreground" />
      <div className="min-w-40 flex-1">
        <p className="font-medium">{token.name}</p>
        <p className="text-xs text-muted-foreground tabular">
          {token.prefix}…, created {day(token.createdAt)}, {token.lastUsedAt ? `last used ${day(token.lastUsedAt)}` : "never used"}, expires {day(token.expiresAt)}
        </p>
      </div>
      <Button variant="ghost" className="glass rounded-full text-destructive" onClick={() => setConfirming(true)}>
        Revoke
      </Button>

      <Dialog open={confirming} onOpenChange={setConfirming}>
        <DialogContent className="glass-strong rounded-3xl sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Revoke &ldquo;{token.name}&rdquo;?</DialogTitle>
            <DialogDescription>Anything using this token stops working on its next request. This can&apos;t be undone: you&apos;d create a new token instead.</DialogDescription>
          </DialogHeader>
          <div className="flex justify-end gap-2">
            <Button variant="ghost" className="rounded-full" onClick={() => setConfirming(false)}>
              Keep it
            </Button>
            <Button variant="destructive" className="rounded-full" onClick={confirm} disabled={revoke.isPending}>
              Revoke token
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </li>
  );
}
