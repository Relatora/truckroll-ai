"use client";

import { ArrowDownRight, ArrowUpRight, Minus } from "lucide-react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Segmented } from "@/components/controls/segmented";
import { ALL_COLOUR, branchColour, money, moneyAxis, monthLabel, PERIODS, useDashboard, type Dashboard, type Metric } from "@/lib/dashboard";
import { cn } from "@/lib/utils";
import { ColumnChart, Legend, LineChart } from "./charts";

/** How a metric reads: its value, how its change is worded, and which direction is good. */
type Kind = { format: (v: number) => string; change: (now: number, before: number) => { text: string; up: boolean } | null; upIsGood: boolean };

const percentChange = (now: number, before: number) =>
  before === 0 ? null : { text: `${Math.abs(Math.round(((now - before) / before) * 100))}%`, up: now >= before };

const KINDS = {
  completed: { format: (v) => v.toLocaleString("en-CA"), change: percentChange, upIsGood: true },
  firstTimeFix: { format: (v) => `${v.toFixed(1)}%`, change: (n, b) => ({ text: `${Math.abs(n - b).toFixed(1)} pts`, up: n >= b }), upIsGood: true },
  revenue: { format: (v) => money(v), change: percentChange, upIsGood: true },
  hoursToDispatch: { format: (v) => `${v.toFixed(1)} h`, change: (n, b) => ({ text: `${Math.abs(n - b).toFixed(1)} h`, up: n >= b }), upIsGood: false },
} satisfies Record<string, Kind>;

/**
 * The branch numbers: the owner's view of every branch, or a manager's of theirs. Filters sit in
 * one row above everything they scope, and live in the URL (?days=&branch=).
 */
export function BranchNumbers({ owner }: { owner: boolean }) {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const days = Number(params.get("days")) || 30;
  const branch = params.get("branch") ?? "";
  const { data, isPending, isPlaceholderData, error } = useDashboard(days, branch);
  const period = PERIODS.find((p) => p.days === days) ?? PERIODS[0];

  function update(changes: Record<string, string>) {
    const next = new URLSearchParams(params);
    Object.entries(changes).forEach(([k, v]) => (v ? next.set(k, v) : next.delete(k)));
    router.replace(`${pathname}${next.size ? `?${next}` : ""}`, { scroll: false });
  }

  return (
    <section aria-labelledby="branch-numbers" className="space-y-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 id="branch-numbers" className="type-title text-3xl">
            {owner && !branch ? "Branch numbers" : data ? `${data.branches[0]?.name} numbers` : "Branch numbers"}
          </h2>
          {data && (
            <p className="text-sm text-muted-foreground">
              {new Date(`${data.window.from}T12:00`).toLocaleDateString("en-CA", { month: "short", day: "numeric" })} to{" "}
              {new Date(`${data.window.to}T12:00`).toLocaleDateString("en-CA", { month: "short", day: "numeric", year: "numeric" })}, compared with the {period.previous}.
            </p>
          )}
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <Segmented id="dashboard-period" label="Period" value={String(days)} onChange={(v) => update({ days: v === "30" ? "" : v })} options={PERIODS.map((p) => ({ value: String(p.days), label: p.label }))} />
        {owner && (
          <label className="glass flex h-11 items-center gap-2 rounded-full pr-2 pl-4 text-sm">
            <span className="text-muted-foreground">Branch</span>
            <select value={branch} onChange={(e) => update({ branch: e.target.value })} className="h-full bg-transparent font-medium outline-none">
              <option value="">All branches</option>
              {["KIT", "GUE", "CAM", "BRA", "STR"].map((code) => (
                <option key={code} value={code}>
                  {{ KIT: "Kitchener", GUE: "Guelph", CAM: "Cambridge", BRA: "Brantford", STR: "Stratford" }[code]}
                </option>
              ))}
            </select>
          </label>
        )}
      </div>

      {error ? (
        <p className="glass rounded-2xl p-5 text-sm text-destructive">Couldn&apos;t load the numbers. Check the API is running, then refresh.</p>
      ) : isPending || !data ? (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
          {Array.from({ length: 5 }).map((_, i) => (
            <div key={i} className="glass h-32 animate-pulse rounded-2xl" />
          ))}
        </div>
      ) : (
        // While a new slice loads, the old one stays put, dimmed: no skeleton, no jump.
        <div className={cn("space-y-5 transition-opacity", isPlaceholderData && "opacity-60")} aria-busy={isPlaceholderData}>
          <Tiles data={data} previous={period.previous} />
          {data.byBranch.length > 1 && <BranchTable data={data} />}
          <Charts data={data} />
        </div>
      )}
    </section>
  );
}

function Tiles({ data, previous }: { data: Dashboard; previous: string }) {
  const t = data.total;
  return (
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
      <Tile label="Jobs completed" metric={t.completed} kind={KINDS.completed} previous={previous} />
      <Tile label="First-time fix" metric={t.firstTimeFix} kind={KINDS.firstTimeFix} previous={previous} />
      <Tile label="Revenue" metric={t.revenue} kind={KINDS.revenue} previous={previous} />
      <Tile label="Hours to dispatch" hint="median, repairs" metric={t.hoursToDispatch} kind={KINDS.hoursToDispatch} previous={previous} />
      <div className="glass rounded-2xl p-4">
        <p className="text-sm text-muted-foreground">Active members</p>
        <p className="mt-1 font-display text-3xl font-semibold tracking-tight">{t.members.active.toLocaleString("en-CA")}</p>
        <p className="mt-2 text-xs text-muted-foreground">
          {t.members.renewingSoon} renew in the next 30 days, {t.members.lapsed} lapsed
        </p>
      </div>
    </div>
  );
}

function Tile({ label, hint, metric, kind, previous }: { label: string; hint?: string; metric: Metric; kind: Kind; previous: string }) {
  return (
    <div className="glass rounded-2xl p-4">
      <p className="text-sm text-muted-foreground">
        {label}
        {hint && <span className="text-xs">, {hint}</span>}
      </p>
      <p className="mt-1 font-display text-3xl font-semibold tracking-tight">{metric.current === null ? "–" : kind.format(metric.current)}</p>
      <Delta metric={metric} kind={kind} className="mt-2" suffix={`vs ${previous}`} />
    </div>
  );
}

/** A signed change: an arrow and words carry the direction; colour says whether it's good. */
function Delta({ metric, kind, className, suffix }: { metric: Metric; kind: Kind; className?: string; suffix?: string }) {
  if (metric.current === null || metric.previous === null) {
    return <p className={cn("text-xs text-muted-foreground", className)}>No earlier figure to compare</p>;
  }

  const change = kind.change(metric.current, metric.previous);
  if (!change || metric.current === metric.previous) {
    return (
      <p className={cn("flex items-center gap-1 text-xs text-muted-foreground", className)}>
        <Minus className="size-3.5" /> No change {suffix}
      </p>
    );
  }

  const good = change.up === kind.upIsGood;
  const Arrow = change.up ? ArrowUpRight : ArrowDownRight;
  return (
    <p className={cn("flex flex-wrap items-center gap-1 text-xs", className)}>
      <span className={cn("inline-flex items-center gap-0.5 font-semibold", good ? "text-success" : "text-destructive")}>
        <Arrow className="size-3.5" aria-hidden />
        <span className="sr-only">{change.up ? "Up" : "Down"}</span>
        {change.text}
      </span>
      {suffix && <span className="text-muted-foreground">{suffix}</span>}
    </p>
  );
}

function BranchTable({ data }: { data: Dashboard }) {
  const columns = [
    { key: "completed", label: "Jobs completed" },
    { key: "firstTimeFix", label: "First-time fix" },
    { key: "revenue", label: "Revenue" },
    { key: "hoursToDispatch", label: "Hours to dispatch" },
  ] as const;

  return (
    <div className="glass overflow-x-auto rounded-3xl">
      <table className="w-full min-w-[40rem] text-sm">
        <caption className="sr-only">Each branch this period, with the change from the period before</caption>
        <thead>
          <tr className="text-left text-xs text-muted-foreground">
            <th className="px-5 pt-4 pb-2 font-medium">Branch</th>
            {columns.map((c) => (
              <th key={c.key} className="px-3 pt-4 pb-2 text-right font-medium">
                {c.label}
              </th>
            ))}
            <th className="px-5 pt-4 pb-2 text-right font-medium">Active members</th>
          </tr>
        </thead>
        <tbody>
          {data.byBranch.map((b) => (
            <tr key={b.code} className="border-t border-foreground/[0.06]">
              <th scope="row" className="px-5 py-3 text-left font-medium">
                <span className="flex items-center gap-2">
                  <span className="size-2.5 rounded-full" style={{ background: branchColour(b.code) }} />
                  {b.name}
                </span>
              </th>
              {columns.map((c) => {
                const metric = b.kpis[c.key];
                return (
                  <td key={c.key} className="px-3 py-3 text-right tabular">
                    <span className="font-medium">{metric.current === null ? "–" : KINDS[c.key].format(metric.current)}</span>
                    <Delta metric={metric} kind={KINDS[c.key]} className="justify-end" />
                  </td>
                );
              })}
              <td className="px-5 py-3 text-right tabular">
                <span className="font-medium">{b.kpis.members.active}</span>
                <p className="text-xs text-muted-foreground">{b.kpis.members.renewingSoon} renewing soon</p>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Charts({ data }: { data: Dashboard }) {
  const series = data.branches.map((b) => ({ code: b.code, name: b.name, colour: branchColour(b.code) }));
  const single = series.length === 1;
  const total = (m: Dashboard["months"][number]) => m.branches.reduce((sum, b) => sum + b.completed, 0);

  return (
    <>
      <div className="grid gap-5 lg:grid-cols-2">
        <figure className="glass space-y-3 rounded-3xl p-5">
          <figcaption>
            <p className="font-semibold">Revenue by month{single ? `, ${series[0].name}` : ""}</p>
            <p className="text-xs text-muted-foreground">Invoiced on completed jobs, last 12 months</p>
          </figcaption>
          {!single && <Legend series={series} />}
          <LineChart
            months={data.months}
            series={series}
            value={(m, code) => m.branches.find((b) => b.code === code)?.revenue ?? 0}
            format={(v) => money(v)}
            axisFormat={moneyAxis}
            label={single ? `Revenue by month for ${series[0].name}, last 12 months` : "Revenue by month for each branch, last 12 months"}
          />
        </figure>
        <figure className="glass space-y-3 rounded-3xl p-5">
          <figcaption>
            <p className="font-semibold">Jobs completed by month{single ? `, ${series[0].name}` : ", all branches"}</p>
            <p className="text-xs text-muted-foreground">Last 12 months</p>
          </figcaption>
          <ColumnChart months={data.months} colour={single ? series[0].colour : ALL_COLOUR} value={total} label="Jobs completed by month, last 12 months" />
        </figure>
      </div>

      {/* The table view: every value the charts show, without hovering. */}
      <details className="glass rounded-3xl p-5 text-sm">
        <summary className="cursor-pointer font-medium">Monthly figures as a table</summary>
        <div className="mt-3 overflow-x-auto">
          <table className="w-full min-w-[36rem] text-right tabular">
            <thead className="text-xs text-muted-foreground">
              <tr>
                <th className="py-1.5 text-left font-medium">Month</th>
                {series.map((s) => (
                  <th key={s.code} className="px-2 py-1.5 font-medium">
                    {s.name} revenue
                  </th>
                ))}
                <th className="py-1.5 pl-2 font-medium">Jobs completed</th>
              </tr>
            </thead>
            <tbody>
              {data.months.map((m) => (
                <tr key={m.month} className="border-t border-foreground/[0.06]">
                  <th scope="row" className="py-1.5 text-left font-medium">
                    {monthLabel(m.month, true)}
                  </th>
                  {series.map((s) => (
                    <td key={s.code} className="px-2 py-1.5">
                      {money(m.branches.find((b) => b.code === s.code)?.revenue ?? 0, false)}
                    </td>
                  ))}
                  <td className="py-1.5 pl-2">{total(m).toLocaleString("en-CA")}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </>
  );
}
