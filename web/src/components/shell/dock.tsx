"use client";

import { AnimatePresence, LayoutGroup, motion } from "motion/react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState } from "react";
import { cn } from "@/lib/utils";
import { useMe } from "@/lib/session";
import { navigationFor } from "./navigation";

/**
 * The app's navigation: a floating glass dock instead of a sidebar.
 *
 * On a desktop it floats at the left edge, vertically centred: icons that lift on hover, a label
 * that slides out beside them, and an active pill that glides between pages. On a phone it's a
 * tab bar along the bottom, where thumbs are. The page keeps the full width either way.
 */
export function Dock() {
  const pathname = usePathname();
  const { data: me } = useMe();
  const items = navigationFor(me);

  return (
    <LayoutGroup id="dock">
      <nav
        aria-label="Main"
        className="glass-strong fixed z-40 flex gap-1 rounded-2xl p-1.5 max-md:inset-x-3 max-md:bottom-3 max-md:justify-around md:top-1/2 md:left-4 md:-translate-y-1/2 md:flex-col"
      >
        <Link
          href="/dashboard"
          aria-label="Truckroll overview"
          className="mb-1 hidden size-11 items-center justify-center rounded-xl bg-[image:var(--thermal)] font-display text-lg font-bold text-white shadow-[0_8px_24px_-8px_var(--violet)] md:flex"
        >
          T
        </Link>
        {items.map((item) => (
          <DockItem
            key={item.href}
            href={item.href}
            label={item.label}
            icon={item.icon}
            active={pathname === item.href || pathname.startsWith(item.href + "/")}
          />
        ))}
      </nav>
    </LayoutGroup>
  );
}

function DockItem({ href, label, icon: Icon, active }: { href: string; label: string; icon: React.ElementType; active: boolean }) {
  const [hover, setHover] = useState(false);

  return (
    <Link
      href={href}
      aria-label={label}
      aria-current={active ? "page" : undefined}
      onPointerEnter={() => setHover(true)}
      onPointerLeave={() => setHover(false)}
      onFocus={() => setHover(true)}
      onBlur={() => setHover(false)}
      className="relative flex size-11 items-center justify-center rounded-xl outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
    >
      {active && (
        <motion.span
          layoutId="dock-active"
          className="absolute inset-0 rounded-xl bg-primary shadow-[0_8px_20px_-8px_var(--primary)]"
          transition={{ type: "spring", stiffness: 500, damping: 36 }}
        />
      )}
      <motion.span
        animate={{ scale: hover ? 1.18 : 1, y: hover ? -1 : 0 }}
        transition={{ type: "spring", stiffness: 600, damping: 22 }}
        className={cn("relative z-10", active ? "text-primary-foreground" : "text-foreground/70")}
      >
        <Icon className="size-[19px]" strokeWidth={active ? 2.3 : 2} />
      </motion.span>

      {/* The label slides out beside the dock on desktop. Phones see aria-label only. */}
      <AnimatePresence>
        {hover && (
          <motion.span
            initial={{ opacity: 0, x: -6, scale: 0.96 }}
            animate={{ opacity: 1, x: 0, scale: 1 }}
            exit={{ opacity: 0, x: -6, scale: 0.96 }}
            transition={{ type: "spring", stiffness: 520, damping: 30 }}
            className="glass-strong pointer-events-none absolute left-[calc(100%+0.75rem)] hidden rounded-lg px-2.5 py-1.5 text-sm font-medium whitespace-nowrap md:block"
          >
            {label}
          </motion.span>
        )}
      </AnimatePresence>
    </Link>
  );
}
