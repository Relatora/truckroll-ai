"use client";

import { motion } from "motion/react";
import Link from "next/link";
import { SpotlightCard } from "@/components/motion/spotlight-card";
import { Reveal, RevealItem } from "@/components/motion/reveal";
import { NotBuiltYet } from "@/components/shell/not-built-yet";
import { navigationFor, toneStyles } from "@/components/shell/navigation";
import { roleLabels, useMe } from "@/lib/session";
import { cn } from "@/lib/utils";

/**
 * Where everyone lands after signing in: a greeting, and every place their day happens as a
 * tile that lights up under the cursor. The owner dashboard (#17) joins it later.
 */
export default function OverviewPage() {
  const { data: me } = useMe();

  if (!me) {
    return <div className="glass h-28 w-full max-w-xl animate-pulse rounded-2xl" aria-label="Loading" />;
  }

  const firstName = me.displayName.split(" ")[0];
  const pages = navigationFor(me).filter((item) => item.href !== "/dashboard");

  return (
    <Reveal className="mx-auto w-full max-w-5xl">
      <RevealItem className="mb-10">
        <p className="text-sm font-medium text-muted-foreground">
          {roleLabels[me.role]}, {me.branch ? `${me.branch.name} branch` : "all five branches"}
        </p>
        <h1 className="type-display mt-2 text-5xl md:text-6xl">
          {greeting()}, {firstName}
        </h1>
      </RevealItem>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {pages.map((item) => (
          <RevealItem key={item.href}>
            <Link href={item.href} className="group block rounded-2xl outline-none focus-visible:ring-3 focus-visible:ring-ring/50">
              <SpotlightCard className="flex h-full flex-col gap-6 p-5 transition-transform duration-300 ease-[var(--ease-spring)] group-hover:-translate-y-1">
                <motion.span
                  whileHover={{ rotate: -8, scale: 1.08 }}
                  className={cn("flex size-11 items-center justify-center rounded-xl", toneStyles[item.tone])}
                >
                  <item.icon className="size-5" />
                </motion.span>
                <span>
                  <span className="block font-display text-lg font-semibold tracking-tight">{item.label}</span>
                  <span className="mt-0.5 block text-sm text-muted-foreground">{item.blurb}</span>
                </span>
              </SpotlightCard>
            </Link>
          </RevealItem>
        ))}
      </div>

      {me.can.canSeeFinancials && (
        <RevealItem className="mt-10">
          <NotBuiltYet
            title="Branch numbers"
            description="Jobs, first-time-fix rate, revenue and renewals across your branches."
            issue={17}
            embedded
          />
        </RevealItem>
      )}
    </Reveal>
  );
}

function greeting() {
  const hour = new Date().getHours();
  return hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
}
