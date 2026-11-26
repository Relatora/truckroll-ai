"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "./api";

/** Mirrors MeResponse in Truckroll.Api/Features/Auth/AuthEndpoints.cs. */
export type Role = "Owner" | "BranchManager" | "Dispatcher" | "Csr" | "Technician";

export type Me = {
  id: string;
  displayName: string;
  email: string;
  role: Role;
  branch: { id: number; code: string; name: string } | null;
  technicianId: number | null;
  can: {
    canDispatch: boolean;
    canReviewCalls: boolean;
    canBookJobs: boolean;
    canSeeFinancials: boolean;
  };
};

export const roleLabels: Record<Role, string> = {
  Owner: "Owner",
  BranchManager: "Branch manager",
  Dispatcher: "Dispatcher",
  Csr: "Customer service",
  Technician: "Technician",
};

/** The signed-in person. Cached for the session; sign-in and sign-out reset it. */
export function useMe() {
  return useQuery({
    queryKey: ["me"],
    queryFn: () => api<Me>("/auth/me"),
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export function signIn(email: string, password: string) {
  return api<Me>("/auth/sign-in", { method: "POST", body: JSON.stringify({ email, password }) });
}

export function signOut() {
  return api<void>("/auth/sign-out", { method: "POST" });
}
