"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { api } from "./api";

/** Mirrors Truckroll.Api/Features/Dashboard/DashboardEndpoints.cs. */

export type Metric = { current: number | null; previous: number | null };
export type Members = { active: number; lapsed: number; renewingSoon: number };
export type Kpis = { completed: Metric; firstTimeFix: Metric; revenue: Metric; hoursToDispatch: Metric; members: Members };
export type DashboardBranch = { id: number; code: string; name: string };
export type MonthPoint = { month: string; branches: { code: string; revenue: number; completed: number }[] };

export type Dashboard = {
  window: { from: string; to: string; days: number };
  branches: DashboardBranch[];
  total: Kpis;
  byBranch: { code: string; name: string; kpis: Kpis }[];
  months: MonthPoint[];
};

export const PERIODS = [
  { days: 30, label: "30 days", previous: "previous 30 days" },
  { days: 90, label: "90 days", previous: "previous 90 days" },
  { days: 365, label: "12 months", previous: "previous 12 months" },
] as const;

/**
 * Each branch keeps one colour wherever it appears, by its fixed place in the company (never by
 * rank or by what's filtered), so Cambridge is always aqua.
 */
const BRANCH_ORDER = ["KIT", "GUE", "CAM", "BRA", "STR"];
export const branchColour = (code: string) => `var(--series-${BRANCH_ORDER.indexOf(code) + 1})`;
export const ALL_COLOUR = "var(--series-all)";

export function useDashboard(days: number, branch: string) {
  const params = new URLSearchParams({ days: String(days) });
  if (branch) params.set("branch", branch);
  // Keep the last numbers on screen while new ones load: no skeleton, no layout jump.
  return useQuery({ queryKey: ["dashboard", days, branch], queryFn: () => api<Dashboard>(`/dashboard?${params}`), placeholderData: keepPreviousData });
}

/** $1,284 / $12.9K / $1.2M: compact once it's wide. */
export function money(value: number, compact = true) {
  if (!compact || Math.abs(value) < 10_000) return value.toLocaleString("en-CA", { style: "currency", currency: "CAD", maximumFractionDigits: 0 });
  return `$${new Intl.NumberFormat("en-CA", { notation: "compact", maximumFractionDigits: 1 }).format(value)}`;
}

/** Axis ticks in one style: all compact once the axis reaches $10K ($0 / $5K / $10K), else in full. */
export function moneyAxis(value: number, top: number) {
  if (top < 10_000 || value === 0) return money(value, false);
  return `$${new Intl.NumberFormat("en-CA", { notation: "compact", maximumFractionDigits: 1 }).format(value)}`;
}

export function monthLabel(month: string, withYear = false) {
  const [y, m] = month.split("-").map(Number);
  return new Date(y, m - 1, 1).toLocaleDateString("en-CA", withYear ? { month: "short", year: "numeric" } : { month: "short" });
}
