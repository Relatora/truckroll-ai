"use client";

import Link from "next/link";
import { PageHeader } from "@/components/shell/page-header";
import { NotBuiltYet } from "@/components/shell/not-built-yet";
import { navigationFor } from "@/components/shell/navigation";
import { roleLabels, useMe } from "@/lib/session";

/**
 * Where everyone lands after signing in. Until the owner dashboard (#17) arrives, it greets the
 * person and lays out where their day happens.
 */
export default function OverviewPage() {
  const { data: me } = useMe();

  if (!me) {
    return <div className="h-24 w-80 animate-pulse rounded-lg bg-muted" aria-label="Loading" />;
  }

  const firstName = me.displayName.split(" ")[0];
  const pages = navigationFor(me).filter((item) => item.href !== "/dashboard");

  return (
    <>
      <PageHeader
        title={`${greeting()}, ${firstName}`}
        description={`${roleLabels[me.role]}, ${me.branch ? `${me.branch.name} branch` : "all five branches"}.`}
      />

      <nav aria-label="Where to start" className="mb-10 grid max-w-4xl gap-px overflow-hidden rounded-xl border bg-border sm:grid-cols-2 lg:grid-cols-3">
        {pages.map((item) => (
          <Link
            key={item.href}
            href={item.href}
            className="group flex items-center gap-3 bg-card px-4 py-3.5 transition-colors hover:bg-accent focus-visible:bg-accent focus-visible:outline-none"
          >
            <item.icon className="size-4 text-muted-foreground transition-colors group-hover:text-foreground" aria-hidden />
            <span className="font-medium">{item.label}</span>
          </Link>
        ))}
      </nav>

      {me.can.canSeeFinancials && (
        <NotBuiltYet
          title="Branch numbers"
          description="Jobs, first-time-fix rate, revenue and renewals across your branches."
          issue={17}
          embedded
        />
      )}
    </>
  );
}

function greeting() {
  const hour = new Date().getHours();
  return hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
}
