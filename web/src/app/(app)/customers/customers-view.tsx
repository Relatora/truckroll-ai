"use client";

import { AnimatePresence, LayoutGroup, motion } from "motion/react";
import { ChevronLeft, ChevronRight, Search, SearchX, X } from "lucide-react";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { PageHeader } from "@/components/shell/page-header";
import { fromNow, planLabels, useBranches, useCustomers, type CustomerListItem } from "@/lib/customers";
import { useMe } from "@/lib/session";
import { cn } from "@/lib/utils";

const MEMBERSHIP = [
  { value: "", label: "Everyone" },
  { value: "active", label: "Members" },
  { value: "lapsed", label: "Lapsed" },
  { value: "none", label: "Not members" },
];

const SORTS = [
  { value: "", label: "Name" },
  { value: "lastVisit", label: "Last visit" },
  { value: "open", label: "Open jobs" },
  { value: "equipment", label: "Equipment" },
];

/**
 * The customer list. Search, filters, sort and page all live in the URL, so a filtered view can
 * be bookmarked, shared, or reached with the back button exactly as it was left.
 */
export function CustomersView() {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const { data: me } = useMe();
  const { data: branches } = useBranches();

  const filters = {
    q: params.get("q") ?? "",
    branch: params.get("branch") ?? "",
    membership: params.get("membership") ?? "",
    sort: params.get("sort") ?? "",
    page: Number(params.get("page") ?? "1"),
  };
  const { data, isPending, isFetching } = useCustomers(filters);

  // Typing updates the box instantly and the URL (and the query) a beat later.
  const [search, setSearch] = useState(filters.q);
  useEffect(() => {
    const handle = setTimeout(() => {
      if (search !== filters.q) update({ q: search, page: "" });
    }, 250);
    return () => clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search]);

  function update(changes: Record<string, string>) {
    const next = new URLSearchParams(params);
    Object.entries(changes).forEach(([key, value]) => (value ? next.set(key, value) : next.delete(key)));
    router.replace(`${pathname}${next.size ? `?${next}` : ""}`, { scroll: false });
  }

  const pageCount = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  const first = data && data.total > 0 ? (data.page - 1) * data.pageSize + 1 : 0;
  const last = data ? Math.min(data.page * data.pageSize, data.total) : 0;

  return (
    <div className="mx-auto w-full max-w-6xl">
      <PageHeader
        title="Customers"
        description={
          me?.role === "Technician"
            ? "The homes you've been sent to, and what's installed there."
            : "Every home you serve, what's installed there, and what went wrong before."
        }
      />

      <div className="mb-5 flex flex-wrap items-center gap-3">
        <label className="glass flex h-11 min-w-[16rem] flex-1 items-center gap-2.5 rounded-full px-4 focus-within:ring-3 focus-within:ring-ring/50 sm:max-w-sm">
          <Search className="size-4 text-muted-foreground" aria-hidden />
          <span className="sr-only">Search customers</span>
          <input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Name, phone or street"
            className="h-full flex-1 bg-transparent text-sm outline-none placeholder:text-muted-foreground"
          />
          {search && (
            <button type="button" onClick={() => setSearch("")} aria-label="Clear the search" className="rounded-full p-1 text-muted-foreground hover:bg-foreground/[0.06] hover:text-foreground">
              <X className="size-3.5" />
            </button>
          )}
        </label>

        <Segmented
          id="membership"
          label="Membership"
          options={MEMBERSHIP}
          value={filters.membership}
          onChange={(value) => update({ membership: value, page: "" })}
        />

        {me?.role === "Owner" && branches && (
          <Picker
            label="Branch"
            value={filters.branch}
            onChange={(value) => update({ branch: value, page: "" })}
            options={[{ value: "", label: "All branches" }, ...branches.map((b) => ({ value: b.code, label: b.name }))]}
          />
        )}

        <Picker label="Sort by" value={filters.sort} onChange={(value) => update({ sort: value, page: "" })} options={SORTS} />
      </div>

      <div className="glass overflow-hidden rounded-3xl">
        <div className="hidden grid-cols-[minmax(0,2.2fr)_minmax(0,2fr)_6rem_7rem_8rem_5rem] gap-4 border-b border-foreground/[0.06] px-5 py-3 text-xs font-semibold text-muted-foreground md:grid">
          <span>Customer</span>
          <span>Home</span>
          <span>Branch</span>
          <span>Membership</span>
          <span>Last visit</span>
          <span className="text-right">Open jobs</span>
        </div>

        <div aria-busy={isFetching} className={cn("transition-opacity duration-300", isFetching && !isPending && "opacity-60")}>
          {isPending ? (
            <SkeletonRows />
          ) : data && data.items.length > 0 ? (
            <motion.ul initial="hidden" animate="shown" key={`${filters.page}-${filters.membership}-${filters.sort}-${filters.branch}`} variants={{ shown: { transition: { staggerChildren: 0.025 } } }}>
              {data.items.map((customer) => (
                <Row key={customer.id} customer={customer} />
              ))}
            </motion.ul>
          ) : (
            <Empty searched={filters.q} onClear={() => { setSearch(""); update({ q: "", membership: "", branch: "", page: "" }); }} />
          )}
        </div>
      </div>

      {data && data.total > 0 && (
        <nav aria-label="Pages" className="mt-5 flex items-center justify-between gap-3 text-sm text-muted-foreground">
          <span className="tabular">
            {first}–{last} of {data.total.toLocaleString("en-CA")}
          </span>
          <span className="flex gap-2">
            <PageButton disabled={data.page <= 1} onClick={() => update({ page: String(data.page - 1) })} label="Previous page">
              <ChevronLeft className="size-4" />
            </PageButton>
            <PageButton disabled={data.page >= pageCount} onClick={() => update({ page: String(data.page + 1) })} label="Next page">
              <ChevronRight className="size-4" />
            </PageButton>
          </span>
        </nav>
      )}
    </div>
  );
}

function Row({ customer }: { customer: CustomerListItem }) {
  const initials = customer.name.split(" ").map((part) => part[0]).join("").slice(0, 2);
  return (
    <motion.li
      variants={{ hidden: { opacity: 0, y: 8 }, shown: { opacity: 1, y: 0, transition: { type: "spring", stiffness: 380, damping: 30 } } }}
      className="border-b border-foreground/[0.05] last:border-0"
    >
      <Link
        href={`/customers/${customer.id}`}
        className="group grid grid-cols-[auto_1fr_auto] items-center gap-x-3 gap-y-1 px-5 py-3.5 outline-none transition-colors hover:bg-foreground/[0.03] focus-visible:bg-foreground/[0.05] md:grid-cols-[minmax(0,2.2fr)_minmax(0,2fr)_6rem_7rem_8rem_5rem] md:gap-4"
      >
        <span className="flex min-w-0 items-center gap-3 max-md:contents">
          <span className={cn("flex size-9 shrink-0 items-center justify-center rounded-xl text-xs font-bold transition-transform duration-300 ease-[var(--ease-spring)] group-hover:scale-110 group-hover:-rotate-6 max-md:col-start-1 max-md:row-span-2 max-md:row-start-1", avatarTone(customer.name))}>
            {initials}
          </span>
          <span className="min-w-0 max-md:col-start-2 max-md:row-start-1">
            <span className="block truncate font-medium">{customer.name}</span>
            <span className="block truncate text-xs text-muted-foreground tabular">{customer.phone}</span>
          </span>
        </span>
        <span className="truncate text-sm text-muted-foreground max-md:col-span-2 max-md:col-start-2 max-md:row-start-2">{customer.address}</span>
        <span className="hidden md:block">
          <span className="rounded-full bg-foreground/[0.06] px-2 py-0.5 text-xs font-semibold">{customer.branchCode}</span>
        </span>
        <span className="hidden md:block">
          {customer.plan ? (
            <span className={cn("rounded-full px-2 py-0.5 text-xs font-semibold", customer.membershipStatus === "Active" ? "bg-success-soft text-success" : "bg-warning-soft text-warning")}>
              {customer.membershipStatus === "Active" ? planLabels[customer.plan].replace("Comfort Club", "Club") : "Lapsed"}
            </span>
          ) : (
            <span className="text-xs text-muted-foreground">None</span>
          )}
        </span>
        <span className="hidden text-sm text-muted-foreground md:block">{customer.lastVisit ? fromNow(customer.lastVisit) : "Never"}</span>
        <span className="text-right max-md:col-start-3 max-md:row-start-1">
          {customer.openJobs > 0 ? (
            <span className="inline-flex min-w-6 justify-center rounded-full bg-ember px-1.5 py-0.5 text-xs font-bold text-white glow-ember">{customer.openJobs}</span>
          ) : (
            <span className="text-xs text-muted-foreground">0</span>
          )}
        </span>
      </Link>
    </motion.li>
  );
}

/** A pill row whose active highlight slides between options. */
function Segmented({ id, label, options, value, onChange }: { id: string; label: string; options: { value: string; label: string }[]; value: string; onChange: (value: string) => void }) {
  return (
    <LayoutGroup id={id}>
      <div role="radiogroup" aria-label={label} className="glass flex h-11 max-w-full items-center gap-0.5 overflow-x-auto rounded-full p-1">
        {options.map((option) => {
          const active = option.value === value;
          return (
            <button
              key={option.value}
              type="button"
              role="radio"
              aria-checked={active}
              onClick={() => onChange(option.value)}
              className={cn("relative h-full shrink-0 rounded-full px-3.5 text-sm font-medium whitespace-nowrap transition-colors focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none", active ? "text-primary-foreground" : "text-muted-foreground hover:text-foreground")}
            >
              {active && <motion.span layoutId={`${id}-pill`} className="absolute inset-0 rounded-full bg-primary" transition={{ type: "spring", stiffness: 500, damping: 36 }} />}
              <span className="relative">{option.label}</span>
            </button>
          );
        })}
      </div>
    </LayoutGroup>
  );
}

function Picker({ label, value, onChange, options }: { label: string; value: string; onChange: (value: string) => void; options: { value: string; label: string }[] }) {
  return (
    <label className="glass flex h-11 items-center gap-2 rounded-full pr-2 pl-4 text-sm focus-within:ring-3 focus-within:ring-ring/50">
      <span className="text-muted-foreground">{label}</span>
      <select value={value} onChange={(event) => onChange(event.target.value)} className="h-full cursor-pointer bg-transparent pr-1 font-medium outline-none">
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </label>
  );
}

function PageButton({ children, label, disabled, onClick }: { children: React.ReactNode; label: string; disabled: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      aria-label={label}
      disabled={disabled}
      onClick={onClick}
      className="glass flex size-10 items-center justify-center rounded-full text-foreground transition-transform duration-200 ease-[var(--ease-spring)] hover:scale-105 active:scale-95 disabled:pointer-events-none disabled:opacity-40"
    >
      {children}
    </button>
  );
}

function SkeletonRows() {
  return (
    <ul aria-label="Loading customers">
      {Array.from({ length: 8 }, (_, index) => (
        <li key={index} className="flex items-center gap-3 border-b border-foreground/[0.05] px-5 py-4 last:border-0">
          <span className="size-9 animate-pulse rounded-xl bg-foreground/[0.07]" />
          <span className="h-3 w-40 animate-pulse rounded-full bg-foreground/[0.07]" style={{ animationDelay: `${index * 80}ms` }} />
          <span className="ml-auto h-3 w-24 animate-pulse rounded-full bg-foreground/[0.05]" />
        </li>
      ))}
    </ul>
  );
}

function Empty({ searched, onClear }: { searched: string; onClear: () => void }) {
  return (
    <AnimatePresence>
      <motion.div initial={{ opacity: 0, scale: 0.97 }} animate={{ opacity: 1, scale: 1 }} className="flex flex-col items-center gap-3 px-6 py-16 text-center">
        <span className="flex size-12 items-center justify-center rounded-2xl bg-foreground/[0.06]">
          <SearchX className="size-5" />
        </span>
        <p className="font-display text-lg font-semibold">{searched ? `Nobody matches "${searched}"` : "No customers match these filters"}</p>
        <p className="max-w-sm text-sm text-muted-foreground">Try a street name or part of a phone number, or clear the filters to see everyone.</p>
        <button type="button" onClick={onClear} className="mt-1 rounded-full bg-primary px-4 py-2 text-sm font-semibold text-primary-foreground transition-transform hover:-translate-y-0.5">
          Clear search and filters
        </button>
      </motion.div>
    </AnimatePresence>
  );
}

const AVATAR_TONES = ["bg-frost-soft text-frost", "bg-ember-soft text-ember", "bg-aqua-soft text-aqua", "bg-violet-soft text-violet"];

/** A stable colour per name, so the same person always looks the same. */
function avatarTone(name: string) {
  let hash = 0;
  for (const char of name) hash = (hash * 31 + char.charCodeAt(0)) | 0;
  return AVATAR_TONES[Math.abs(hash) % AVATAR_TONES.length];
}
