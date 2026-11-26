"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { ApiStatus } from "@/components/api-status";
import { Button } from "@/components/ui/button";
import { roleLabels, signOut, useMe } from "@/lib/session";

/**
 * A temporary signed-in page that proves the session works end to end. The app shell and the
 * real dashboard replace it in #6 and #17.
 */
export default function DashboardPage() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { data: me, isPending } = useMe();

  async function leave() {
    await signOut();
    queryClient.clear();
    router.replace("/sign-in");
  }

  return (
    <main className="mx-auto flex w-full max-w-2xl flex-1 flex-col justify-center gap-6 p-8">
      {isPending || !me ? (
        <p className="text-muted-foreground">Loading your session…</p>
      ) : (
        <div className="space-y-2">
          <h1 className="text-3xl font-semibold tracking-tight">Hi, {me.displayName.split(" ")[0]}</h1>
          <p className="text-muted-foreground">
            {roleLabels[me.role]}
            {me.branch ? `, ${me.branch.name}` : ", all branches"}
          </p>
          <Button variant="outline" onClick={leave}>
            Sign out
          </Button>
        </div>
      )}
      <ApiStatus />
    </main>
  );
}
