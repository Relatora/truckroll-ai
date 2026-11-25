import { ApiStatus } from "@/components/api-status";

/**
 * Placeholder home page for the scaffold (#2). The real landing page arrives with the
 * design system in #6; until then this proves the stack is wired end to end.
 */
export default function Home() {
  return (
    <main className="mx-auto flex w-full max-w-2xl flex-1 flex-col justify-center gap-6 p-8">
      <div className="space-y-2">
        <h1 className="text-3xl font-semibold tracking-tight">Truckroll</h1>
        <p className="text-muted-foreground">
          A field-service CRM for heating, cooling and plumbing companies.
        </p>
      </div>
      <ApiStatus />
    </main>
  );
}
