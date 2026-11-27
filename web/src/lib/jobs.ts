"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";
import type { EquipmentKind, JobPriority, JobStatus, JobType } from "./customers";

/** Mirrors the records in Truckroll.Api/Features/Jobs/JobEndpoints.cs. */

export type JobListItem = {
  id: number;
  number: string;
  type: JobType;
  priority: JobPriority;
  status: JobStatus;
  windowStart: string;
  windowEnd: string;
  summary: string;
  customerId: number;
  customer: string;
  address: string;
  branchCode: string;
  equipmentKind: EquipmentKind | null;
  technician: string | null;
  technicianColor: string | null;
};

export type JobPage = { items: JobListItem[]; total: number; page: number; pageSize: number };

export type JobNote = { id: number; author: string; source: "Office" | "Technician" | "Mcp"; body: string; createdAt: string };

export type JobDetail = {
  id: number;
  number: string;
  type: JobType;
  priority: JobPriority;
  status: JobStatus;
  windowStart: string;
  windowEnd: string;
  estimatedMinutes: number;
  summary: string;
  resolution: string | null;
  invoiceTotal: number | null;
  firstTimeFix: boolean | null;
  requiredSkill: string;
  requiredCertification: string;
  createdAt: string;
  dispatchedAt: string | null;
  completedAt: string | null;
  branch: { code: string; name: string };
  customer: { id: number; name: string; phone: string };
  property: { street: string; city: string; accessNotes: string | null };
  equipment: { id: number; kind: EquipmentKind; brand: string; model: string; serialNumber: string; installedOn: string; warrantyExpiresOn: string | null } | null;
  technician: { id: number; name: string; color: string } | null;
  notes: JobNote[];
  nextStatuses: JobStatus[];
  canAddNotes: boolean;
};

export type JobFilters = { day?: string; status?: string; q?: string; page?: number };

export function useJobs(filters: JobFilters) {
  const params = new URLSearchParams();
  Object.entries(filters).forEach(([key, value]) => {
    if (value !== undefined && value !== "") params.set(key, String(value));
  });
  params.set("pageSize", "25");

  return useQuery({
    queryKey: ["jobs", "list", params.toString()],
    queryFn: () => api<JobPage>(`/jobs?${params}`),
    placeholderData: keepPreviousData,
  });
}

export function useJob(id: number) {
  return useQuery({ queryKey: ["jobs", "detail", id], queryFn: () => api<JobDetail>(`/jobs/${id}`), retry: false });
}

/** A message drafted for the customer because of something that happened to the job. Never sent. */
export type CustomerUpdate = { id: number; kind: string; subject: string; body: string; createdAt: string };

/**
 * The drafts for a job's customer. They're written by a subscriber to the job event stream, a
 * moment after the change that caused them, so the list checks back while the page is open.
 */
export function useCustomerUpdates(jobId: number) {
  return useQuery({
    queryKey: ["jobs", "customer-updates", jobId],
    queryFn: () => api<CustomerUpdate[]>(`/jobs/${jobId}/customer-updates`),
    refetchInterval: 10_000,
  });
}

/** After any change to a job, the lists and the customer's history are stale too. */
function useInvalidateJobs() {
  const client = useQueryClient();
  return (job: JobDetail) => {
    client.setQueryData(["jobs", "detail", job.id], job);
    client.invalidateQueries({ queryKey: ["jobs", "list"] });
    client.invalidateQueries({ queryKey: ["customers"] });
  };
}

export type ChangeStatus = { status: JobStatus; resolution?: string; invoiceTotal?: number; firstTimeFix?: boolean };

export function useChangeStatus(id: number) {
  const done = useInvalidateJobs();
  return useMutation({
    mutationFn: (change: ChangeStatus) => api<JobDetail>(`/jobs/${id}/status`, { method: "POST", body: JSON.stringify(change) }),
    onSuccess: done,
  });
}

export function useAddNote(id: number) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: string) => api<JobNote>(`/jobs/${id}/notes`, { method: "POST", body: JSON.stringify({ body }) }),
    onSuccess: (note) =>
      client.setQueryData<JobDetail>(["jobs", "detail", id], (job) => (job ? { ...job, notes: [...job.notes, note] } : job)),
  });
}

export type BookJob = {
  propertyId: number;
  equipmentId: number | null;
  type: JobType;
  priority: JobPriority;
  date: string;
  windowStartHour: number;
  summary: string;
};

export function useBookJob() {
  const done = useInvalidateJobs();
  return useMutation({
    mutationFn: (booking: BookJob) => api<JobDetail>("/jobs", { method: "POST", body: JSON.stringify(booking) }),
    onSuccess: done,
  });
}

export const statusLabels: Record<JobStatus, string> = {
  New: "New",
  Scheduled: "Scheduled",
  Dispatched: "Assigned",
  InProgress: "On site",
  Completed: "Done",
  Cancelled: "Cancelled",
};

/** The words on the button that moves a job to this status. Same verb as the toast afterwards. */
export const statusActions: Partial<Record<JobStatus, { label: string; done: string }>> = {
  InProgress: { label: "Start the job", done: "Started" },
  Completed: { label: "Complete the job", done: "Completed" },
  Cancelled: { label: "Cancel the job", done: "Cancelled" },
  Scheduled: { label: "Schedule", done: "Scheduled" },
};

export const WINDOWS = [
  { hour: 8, label: "8 to 10 am" },
  { hour: 10, label: "10 am to noon" },
  { hour: 12, label: "Noon to 2 pm" },
  { hour: 14, label: "2 to 4 pm" },
];

export function windowLabel(startIso: string, endIso: string) {
  const options: Intl.DateTimeFormatOptions = { hour: "numeric", minute: "2-digit" };
  return `${new Date(startIso).toLocaleTimeString("en-CA", options)} to ${new Date(endIso).toLocaleTimeString("en-CA", options)}`;
}
