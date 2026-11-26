import Link from "next/link";
import { Suspense } from "react";
import type { Metadata } from "next";
import { Wordmark } from "@/components/brand/wordmark";
import { SignInForm } from "./sign-in-form";

export const metadata: Metadata = { title: "Sign in" };

/** Sign-in: the navy brand panel beside the form, collapsing to the form alone on a phone. */
export default function SignInPage() {
  return (
    <main className="grid flex-1 lg:grid-cols-[minmax(0,5fr)_minmax(0,6fr)]">
      <section className="hidden flex-col justify-between bg-[#14213d] p-10 text-white dark:bg-[#0a1424] lg:flex">
        <Link href="/" aria-label="Truckroll home" className="w-fit">
          <Wordmark className="text-2xl" inverted />
        </Link>
        <div className="max-w-md">
          <p className="type-display text-4xl">Five branches. Twelve trucks. One board.</p>
          <p className="mt-5 leading-relaxed text-slate-300">
            You&apos;re signing in to Kestrel Home Comfort, a heating, cooling and plumbing company made up for this
            demo. Pick any role to see the app the way that person does.
          </p>
        </div>
        <p className="text-sm text-slate-400">Every customer, address and phone number here is invented.</p>
      </section>

      <section className="flex items-center justify-center px-6 py-12">
        <div className="w-full max-w-sm space-y-8">
          <div className="space-y-2">
            <Link href="/" aria-label="Truckroll home" className="inline-block lg:hidden">
              <Wordmark className="text-xl" />
            </Link>
            <h1 className="type-title text-3xl">Sign in</h1>
            <p className="text-muted-foreground">Use your work email, or pick a demo account below.</p>
          </div>
          {/* useSearchParams reads ?next=, which needs a Suspense boundary under Cache Components. */}
          <Suspense>
            <SignInForm />
          </Suspense>
        </div>
      </section>
    </main>
  );
}
