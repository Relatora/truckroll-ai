"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useRouter, useSearchParams } from "next/navigation";
import { useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api";
import { signIn } from "@/lib/session";

/** One click fills the form. It's a demo with invented people, so the logins are public. */
const DEMO_ACCOUNTS = [
  { email: "morgan@kestrel.test", who: "Morgan Reid", role: "Owner, all branches" },
  { email: "pat@kestrel.test", who: "Pat Gallagher", role: "Branch manager, Kitchener" },
  { email: "alex@kestrel.test", who: "Alex Romero", role: "Dispatcher, Guelph" },
  { email: "dana@kestrel.test", who: "Dana Whitfield", role: "Customer service, Guelph" },
  { email: "nadia@kestrel.test", who: "Nadia Kowalczyk", role: "Technician, Guelph" },
];
const DEMO_PASSWORD = "truckroll-demo";

export function SignInForm() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const next = safeNext(useSearchParams().get("next"));

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setPending(true);
    try {
      const me = await signIn(email, password);
      queryClient.setQueryData(["me"], me);
      router.replace(next);
    } catch (caught) {
      setError(
        caught instanceof ApiError && caught.status === 401
          ? "That email and password don't match. After five tries the account locks for five minutes."
          : "Couldn't reach Truckroll. Check the API is running and try again.",
      );
      setPending(false);
    }
  }

  return (
    <div className="space-y-8">
      <form onSubmit={submit} className="space-y-4" noValidate>
        <div className="space-y-2">
          <Label htmlFor="email">Email</Label>
          <Input
            id="email"
            type="email"
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="password">Password</Label>
          <Input
            id="password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
        </div>
        {error && (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}
        <Button type="submit" className="w-full" disabled={pending || !email || !password}>
          {pending ? "Signing in…" : "Sign in"}
        </Button>
      </form>

      <section aria-labelledby="demo-accounts" className="space-y-2">
        <h2 id="demo-accounts" className="text-sm font-medium">
          Demo accounts
        </h2>
        <ul className="divide-y rounded-lg border">
          {DEMO_ACCOUNTS.map((account) => (
            <li key={account.email}>
              <button
                type="button"
                // Without this, screen readers run the two spans together: "Dana WhitfieldCustomer service".
                aria-label={`Use ${account.who}, ${account.role}`}
                className="flex w-full items-baseline justify-between gap-4 px-3 py-2 text-left text-sm hover:bg-muted focus-visible:bg-muted focus-visible:outline-none"
                onClick={() => {
                  setEmail(account.email);
                  setPassword(DEMO_PASSWORD);
                }}
              >
                <span className="font-medium">{account.who}</span>
                <span className="text-muted-foreground">{account.role}</span>
              </button>
            </li>
          ))}
        </ul>
        <p className="text-xs text-muted-foreground">
          Picking one fills in the form. The password for all of them is <code>{DEMO_PASSWORD}</code>.
        </p>
      </section>
    </div>
  );
}

/** Only same-site paths, so a crafted ?next= can't bounce someone to another site after sign-in. */
function safeNext(next: string | null): string {
  return next && next.startsWith("/") && !next.startsWith("//") ? next : "/dashboard";
}
