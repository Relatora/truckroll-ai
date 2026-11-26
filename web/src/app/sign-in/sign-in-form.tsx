"use client";

import { useQueryClient } from "@tanstack/react-query";
import { AnimatePresence, motion } from "motion/react";
import { Check, Loader2 } from "lucide-react";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api";
import { signIn } from "@/lib/session";
import { cn } from "@/lib/utils";

/** One tap fills the form. It's a demo with invented people, so the logins are public. */
const DEMO_ACCOUNTS = [
  { email: "morgan@kestrel.test", who: "Morgan Reid", role: "Owner", where: "All branches", tone: "bg-violet-soft text-violet" },
  { email: "pat@kestrel.test", who: "Pat Gallagher", role: "Branch manager", where: "Kitchener", tone: "bg-frost-soft text-frost" },
  { email: "alex@kestrel.test", who: "Alex Romero", role: "Dispatcher", where: "Guelph", tone: "bg-aqua-soft text-aqua" },
  { email: "dana@kestrel.test", who: "Dana Whitfield", role: "Customer service", where: "Guelph", tone: "bg-ember-soft text-ember" },
  { email: "nadia@kestrel.test", who: "Nadia Kowalczyk", role: "Technician", where: "Guelph", tone: "bg-foreground/[0.07] text-foreground" },
];
const DEMO_PASSWORD = "truckroll-demo";

export function SignInForm() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const next = safeNext(useSearchParams().get("next"));

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [picked, setPicked] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [state, setState] = useState<"idle" | "pending" | "done">("idle");
  const submitButton = useRef<HTMLButtonElement>(null);
  const typing = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => () => clearInterval(typing.current ?? undefined), []);

  /** Types the account in, quickly, so it's clear what the button did. Then focus moves to submit. */
  function pick(account: (typeof DEMO_ACCOUNTS)[number]) {
    // Pressing the same account again (Enter on a still-focused card) mustn't restart the typing.
    if (picked === account.email && email.length > 0) {
      if (password) {
        submitButton.current?.focus();
      }
      return;
    }
    clearInterval(typing.current ?? undefined);
    setPicked(account.email);
    setError(null);
    setPassword("");

    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      setEmail(account.email);
      setPassword(DEMO_PASSWORD);
      requestAnimationFrame(() => submitButton.current?.focus());
      return;
    }

    let i = 0;
    typing.current = setInterval(() => {
      i += 1;
      setEmail(account.email.slice(0, i));
      if (i >= account.email.length) {
        clearInterval(typing.current ?? undefined);
        setPassword(DEMO_PASSWORD);
        requestAnimationFrame(() => submitButton.current?.focus());
      }
    }, 18);
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setState("pending");
    try {
      const me = await signIn(email, password);
      queryClient.setQueryData(["me"], me);
      setState("done");
      // A beat to see the tick, then in.
      setTimeout(() => router.replace(next), 450);
    } catch (caught) {
      setError(
        caught instanceof ApiError && caught.status === 401
          ? "That email and password don't match. After five tries the account locks for five minutes."
          : "Couldn't reach Truckroll. Check the API is running and try again.",
      );
      setState("idle");
    }
  }

  return (
    <motion.div
      initial={{ opacity: 0, y: 24, scale: 0.97, filter: "blur(10px)" }}
      animate={
        state === "done"
          ? { opacity: 0, y: -16, scale: 1.03, filter: "blur(8px)", transition: { duration: 0.4, delay: 0.25 } }
          : { opacity: 1, y: 0, scale: 1, filter: "blur(0px)" }
      }
      transition={{ type: "spring", stiffness: 220, damping: 24 }}
      className="glass-strong w-full max-w-[56rem] rounded-3xl p-2"
    >
      <div className="grid gap-2 md:grid-cols-[1fr_1.1fr]">
        <form onSubmit={submit} className="flex flex-col gap-5 rounded-[1.25rem] p-6 md:p-8" noValidate>
          <div className="space-y-2">
            <h1 className="type-display text-4xl">Welcome back</h1>
            <p className="text-muted-foreground">Sign in to Kestrel Home Comfort, the demo company.</p>
          </div>
          <div className="space-y-2">
            <Label htmlFor="email">Email</Label>
            <Input id="email" type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} className="h-11 rounded-xl" required />
          </div>
          <div className="space-y-2">
            <Label htmlFor="password">Password</Label>
            <Input id="password" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} className="h-11 rounded-xl" required />
          </div>
          <AnimatePresence>
            {error && (
              <motion.p
                role="alert"
                initial={{ opacity: 0, height: 0 }}
                animate={{ opacity: 1, height: "auto", x: [0, -8, 8, -5, 5, 0] }}
                exit={{ opacity: 0, height: 0 }}
                transition={{ duration: 0.4 }}
                className="text-sm text-destructive"
              >
                {error}
              </motion.p>
            )}
          </AnimatePresence>
          <Button ref={submitButton} type="submit" variant="thermal" size="xl" className="mt-1 w-full" disabled={state !== "idle" || !email || !password}>
            {state === "pending" ? (
              <Loader2 className="size-5 animate-spin" />
            ) : state === "done" ? (
              <motion.span initial={{ scale: 0 }} animate={{ scale: 1 }} className="inline-flex">
                <Check className="size-5" />
              </motion.span>
            ) : (
              "Sign in"
            )}
          </Button>
        </form>

        <section aria-labelledby="demo-accounts" className="rounded-[1.25rem] bg-foreground/[0.03] p-6 md:p-8">
          <h2 id="demo-accounts" className="font-display text-lg font-semibold">
            Try it as anyone
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Each role sees a different Truckroll. The password for all of them is{" "}
            <code className="whitespace-nowrap rounded bg-foreground/[0.06] px-1.5 py-0.5 text-foreground">{DEMO_PASSWORD}</code>.
          </p>
          <ul className="mt-5 grid gap-2">
            {DEMO_ACCOUNTS.map((account, index) => {
              const active = picked === account.email;
              return (
                <motion.li
                  key={account.email}
                  initial={{ opacity: 0, x: 16 }}
                  animate={{ opacity: 1, x: 0 }}
                  transition={{ delay: 0.15 + index * 0.05, type: "spring", stiffness: 300, damping: 26 }}
                >
                  <button
                    type="button"
                    aria-label={`Use ${account.who}, ${account.role}, ${account.where}`}
                    aria-pressed={active}
                    onClick={() => pick(account)}
                    className={cn(
                      "group flex w-full items-center gap-3 rounded-xl border bg-card/60 px-3 py-2.5 text-left transition-[transform,box-shadow,border-color] duration-300 ease-[var(--ease-spring)] hover:-translate-y-0.5 hover:shadow-float focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none active:scale-[0.98]",
                      active ? "border-frost shadow-float" : "border-transparent",
                    )}
                  >
                    <span className={cn("flex size-9 shrink-0 items-center justify-center rounded-lg text-sm font-bold transition-transform duration-300 group-hover:rotate-[-6deg]", account.tone)}>
                      {account.who.split(" ").map((part) => part[0]).join("")}
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate font-medium">{account.who}</span>
                      <span className="block truncate text-sm text-muted-foreground">
                        {account.role}, {account.where}
                      </span>
                    </span>
                    {active && (
                      <motion.span initial={{ scale: 0, rotate: -45 }} animate={{ scale: 1, rotate: 0 }} className="text-frost">
                        <Check className="size-4" />
                      </motion.span>
                    )}
                  </button>
                </motion.li>
              );
            })}
          </ul>
        </section>
      </div>
    </motion.div>
  );
}

/** Only same-site paths, so a crafted ?next= can't bounce someone to another site after sign-in. */
function safeNext(next: string | null): string {
  return next && next.startsWith("/") && !next.startsWith("//") ? next : "/dashboard";
}
