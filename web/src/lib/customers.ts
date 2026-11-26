"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { api } from "./api";

/** Mirrors the records in Truckroll.Api/Features/Customers/CustomerEndpoints.cs. */

export type MembershipPlan = "ComfortClub" | "ComfortClubPlus";
export type MembershipStatus = "Active" | "Lapsed" | "Cancelled";
export type EquipmentKind =
  | "Furnace"
  | "AirConditioner"
  | "HeatPump"
  | "Boiler"
  | "WaterHeater"
  | "TanklessWaterHeater"
  | "Humidifier"
  | "AirExchanger";
export type WarrantyState = "Covered" | "Expiring" | "Expired" | "Unknown";
export type JobType = "Repair" | "Maintenance" | "Installation" | "Estimate";
export type JobPriority = "Emergency" | "Urgent" | "Normal";
export type JobStatus = "New" | "Scheduled" | "Dispatched" | "InProgress" | "Completed" | "Cancelled";

export type CustomerListItem = {
  id: number;
  name: string;
  phone: string;
  address: string | null;
  branchCode: string;
  plan: MembershipPlan | null;
  membershipStatus: MembershipStatus | null;
  equipment: number;
  lastVisit: string | null;
  openJobs: number;
};

export type CustomerPage = { items: CustomerListItem[]; total: number; page: number; pageSize: number };

export type EquipmentDetail = {
  id: number;
  kind: EquipmentKind;
  brand: string;
  model: string;
  serialNumber: string;
  installedOn: string;
  ageYears: number;
  installedByUs: boolean;
  warrantyExpiresOn: string | null;
  warranty: WarrantyState;
  /** Share of the warranty term still to run, 1 to 0; null when unknown. */
  warrantyLeft: number | null;
  manualCode: string | null;
};

export type TimelineJob = {
  id: number;
  number: string;
  type: JobType;
  priority: JobPriority;
  status: JobStatus;
  windowStart: string;
  windowEnd: string;
  summary: string;
  resolution: string | null;
  technician: string | null;
  invoiceTotal: number | null;
  firstTimeFix: boolean | null;
  equipment: string | null;
  equipmentKind: EquipmentKind | null;
  notes: { author: string; source: "Office" | "Technician" | "Mcp"; body: string; createdAt: string }[];
};

export type CustomerDetail = {
  id: number;
  firstName: string;
  lastName: string;
  email: string;
  phone: string;
  source: string;
  customerSince: string;
  branch: { code: string; name: string; phone: string };
  membership: { plan: MembershipPlan; status: MembershipStatus; startedOn: string; renewsOn: string; monthlyPrice: number } | null;
  properties: {
    id: number;
    street: string;
    city: string;
    postalCode: string;
    kind: string;
    yearBuilt: number | null;
    accessNotes: string | null;
    equipment: EquipmentDetail[];
  }[];
  stats: { visits: number; lifetimeRevenue: number; firstTimeFixRate: number | null; openJobs: number };
  timeline: TimelineJob[];
};

export type CustomerFilters = {
  q?: string;
  branch?: string;
  membership?: string;
  sort?: string;
  page?: number;
};

export function useCustomers(filters: CustomerFilters) {
  const params = new URLSearchParams();
  Object.entries(filters).forEach(([key, value]) => {
    if (value !== undefined && value !== "" && value !== null) params.set(key, String(value));
  });
  params.set("pageSize", "20");

  return useQuery({
    queryKey: ["customers", "list", params.toString()],
    queryFn: () => api<CustomerPage>(`/customers?${params}`),
    // Keep showing the last page while the next one loads, so the table never flashes empty.
    placeholderData: keepPreviousData,
  });
}

export function useCustomer(id: number) {
  return useQuery({
    queryKey: ["customers", "detail", id],
    queryFn: () => api<CustomerDetail>(`/customers/${id}`),
    retry: false,
  });
}

export function useBranches() {
  return useQuery({
    queryKey: ["branches"],
    queryFn: () => api<{ code: string; name: string }[]>("/branches"),
    staleTime: Infinity,
  });
}

export const planLabels: Record<MembershipPlan, string> = {
  ComfortClub: "Comfort Club",
  ComfortClubPlus: "Comfort Club Plus",
};

export const equipmentLabels: Record<EquipmentKind, string> = {
  Furnace: "Furnace",
  AirConditioner: "Air conditioner",
  HeatPump: "Heat pump",
  Boiler: "Boiler",
  WaterHeater: "Water heater",
  TanklessWaterHeater: "Tankless water heater",
  Humidifier: "Humidifier",
  AirExchanger: "Air exchanger",
};

/** The trade colour of a piece of equipment: heat, cooling, water or air. */
export function equipmentTone(kind: EquipmentKind | null): "ember" | "frost" | "aqua" | "violet" {
  switch (kind) {
    case "Furnace":
    case "Boiler":
      return "ember";
    case "AirConditioner":
    case "HeatPump":
      return "frost";
    case "WaterHeater":
    case "TanklessWaterHeater":
      return "aqua";
    default:
      return "violet";
  }
}

const relative = new Intl.RelativeTimeFormat("en-CA", { numeric: "auto" });

/** "3 weeks ago", "yesterday", "in 2 days": how people talk about visits. */
export function fromNow(iso: string) {
  const days = Math.round((new Date(iso).getTime() - Date.now()) / 86_400_000);
  if (Math.abs(days) < 7) return relative.format(days, "day");
  if (Math.abs(days) < 60) return relative.format(Math.round(days / 7), "week");
  if (Math.abs(days) < 730) return relative.format(Math.round(days / 30), "month");
  return relative.format(Math.round(days / 365), "year");
}
