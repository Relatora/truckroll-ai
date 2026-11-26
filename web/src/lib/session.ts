"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "./api";
import { useHydrated } from "./use-hydrated";

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

/**
 * The signed-in person. Cached for the session; sign-in and sign-out reset it.
 *
 * The server never knows who's signed in (the page shell is the same for everyone), so until
 * hydration finishes this reports "still loading" even if the cache already has the answer.
 * Otherwise the first client render could differ from the server's HTML.
 */
export function useMe() {
  const hydrated = useHydrated();
  const query = useQuery({
    queryKey: ["me"],
    queryFn: () => api<Me>("/auth/me"),
    staleTime: 5 * 60_000,
    retry: false,
  });
  return hydrated ? query : { ...query, data: undefined, isPending: true as const };
}

export function signIn(email: string, password: string) {
  return api<Me>("/auth/sign-in", { method: "POST", body: JSON.stringify({ email, password }) });
}

export function signOut() {
  return api<void>("/auth/sign-out", { method: "POST" });
}
