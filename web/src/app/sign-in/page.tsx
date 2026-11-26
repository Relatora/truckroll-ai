import Link from "next/link";
import { Suspense } from "react";
import type { Metadata } from "next";
import { Aurora } from "@/components/brand/aurora";
import { Wordmark } from "@/components/brand/wordmark";
import { ThemeSwitch } from "@/components/motion/theme-switch";
import { SignInForm } from "./sign-in-form";

export const metadata: Metadata = { title: "Sign in" };

/** Sign-in: a single glass card floating in the aurora, with every demo role one tap away. */
export default function SignInPage() {
  return (
    <main className="relative flex flex-1 flex-col">
      <Aurora />
      <header className="flex items-center justify-between px-6 py-5">
        <Link href="/" aria-label="Truckroll home" className="rounded-lg text-xl outline-none focus-visible:ring-3 focus-visible:ring-ring/50">
          <Wordmark />
        </Link>
        <ThemeSwitch />
      </header>
      <div className="flex flex-1 items-center justify-center px-4 pb-12">
        {/* useSearchParams reads ?next=, which needs a Suspense boundary under Cache Components. */}
        <Suspense>
          <SignInForm />
        </Suspense>
      </div>
    </main>
  );
}
