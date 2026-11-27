"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";
import type { EquipmentKind, JobPriority, JobType } from "./customers";

/** Mirrors Truckroll.Api/Features/Calls. */

export type CallStatus = "New" | "AwaitingReview" | "Approved" | "Dismissed";

export type CustomerSource = "Referral" | "GoogleSearch" | "GoogleReviews" | "Facebook" | "YardSign" | "TruckWrap" | "HomeShow" | "RepeatCustomer";

export type CallListItem = { id: number; branchCode: string; receivedAt: string; status: CallStatus; caller: string | null; priority: JobPriority | null; preview: string };

export type CustomerOption = { customerId: number; name: string; propertyId: number; address: string; equipment: { id: number; kind: EquipmentKind; label: string }[] };

export type CallProposal = {
  callerName: string;
  street: string | null;
  callerEmail: string | null;
  newCustomer: boolean;
  match: CustomerOption | null;
  candidates: CustomerOption[];
  type: JobType;
  priority: JobPriority;
  equipment: EquipmentKind | null;
  equipmentId: number | null;
  symptoms: string;
  date: string | null;
  windowStartHour: number | null;
  safetyConcern: boolean;
  notes: string[];
  model: string | null;
  attempts: number;
};

export type CallDetail = {
  id: number;
  branch: { code: string; name: string; phone: string };
  receivedAt: string;
  status: CallStatus;
  transcript: string;
  summary: string | null;
  proposal: CallProposal | null;
  followUpSubject: string | null;
  followUpBody: string | null;
  createdJob: { id: number; number: string } | null;
  reviewedBy: string | null;
  reviewedAt: string | null;
};

export type ApproveCall = {
  propertyId?: number;
  newCustomer?: { firstName: string; lastName: string; street: string; email?: string; source?: CustomerSource };
  equipmentId?: number | null;
  type: JobType;
  priority: JobPriority;
  date: string;
  windowStartHour: number;
  summary: string;
  followUpSubject: string;
  followUpBody: string;
};

/** Which calls each tab of the inbox shows. */
export const TABS = {
  inbox: ["New", "AwaitingReview"],
  outbox: ["Approved"],
  closed: ["Dismissed"],
} as const satisfies Record<string, CallStatus[]>;

export function useCalls() {
  return useQuery({ queryKey: ["calls"], queryFn: () => api<CallListItem[]>("/calls") });
}

export function useCall(id: number | null) {
  return useQuery({ queryKey: ["calls", id], queryFn: () => api<CallDetail>(`/calls/${id}`), enabled: id !== null });
}

/** Every change to a call refreshes the list and that call; approving also books a job. */
function useCallMutation<T>(id: number, run: (body: T) => Promise<CallDetail>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: (call) => client.setQueryData(["calls", id], call),
    onSettled: () => {
      client.invalidateQueries({ queryKey: ["calls"], exact: true });
      client.invalidateQueries({ queryKey: ["jobs"] });
      client.invalidateQueries({ queryKey: ["dispatch"] });
    },
  });
}

export const useAnalyse = (id: number) => useCallMutation<void>(id, () => api<CallDetail>(`/calls/${id}/analyse`, { method: "POST" }));
export const useApprove = (id: number) => useCallMutation<ApproveCall>(id, (body) => api<CallDetail>(`/calls/${id}/approve`, { method: "POST", body: JSON.stringify(body) }));
export const useDismiss = (id: number) => useCallMutation<void>(id, () => api<CallDetail>(`/calls/${id}/dismiss`, { method: "POST" }));

export function usePasteCall() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { branchCode?: string; transcript: string }) => api<CallDetail>("/calls", { method: "POST", body: JSON.stringify(body) }),
    onSettled: () => client.invalidateQueries({ queryKey: ["calls"], exact: true }),
  });
}
