import { Suspense } from "react";
import type { Metadata } from "next";
import { SignInForm } from "./sign-in-form";

export const metadata: Metadata = { title: "Sign in · Truckroll" };

/** Sign-in. The real look arrives with the design system in #6. */
export default function SignInPage() {
  return (
    <main className="mx-auto flex w-full max-w-sm flex-1 flex-col justify-center gap-6 p-6">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Sign in to Truckroll</h1>
        <p className="text-sm text-muted-foreground">Kestrel Home Comfort · demo company</p>
      </div>
      {/* useSearchParams reads ?next=, which needs a Suspense boundary under Cache Components. */}
      <Suspense>
        <SignInForm />
      </Suspense>
    </main>
  );
}
