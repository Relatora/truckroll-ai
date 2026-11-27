"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";

/** Mirrors Truckroll.Api/Features/Dispatch/Suggestions. */

export type ScoreParts = { skill: number; distance: number; fit: number; workload: number };

export type SuggestionFacts = {
  hasSkill: boolean;
  kilometres: number;
  comingFrom: string;
  jobsThatDay: number;
  freeFrom: string;
  arrives: string;
  minutesLate: number;
  makesLateFor: string | null;
};

export type Suggestion = { rank: number; technicianId: number; name: string; color: string; score: number; parts: ScoreParts; facts: SuggestionFacts; sentence: string };

export type Suggestions = {
  jobId: number;
  number: string;
  canAssign: boolean;
  currentTechnicianId: number | null;
  top: Suggestion[];
  ruledOut: { technicianId: number; name: string; reason: string }[];
  considered: number;
};

export type Explanation = { technicianId: number; sentence: string; writtenBy: "model" | "rules" };

/**
 * The weights the server ranks with (DispatchScorer.cs), so the score bar can show how much each
 * part contributed. Display only: the order always comes from the server.
 */
export const WEIGHTS: ScoreParts = { skill: 0.3, distance: 0.25, fit: 0.3, workload: 0.15 };

export function useSuggestions(jobId: number, enabled: boolean) {
  return useQuery({ queryKey: ["suggestions", jobId], queryFn: () => api<Suggestions>(`/jobs/${jobId}/suggestions`), enabled });
}

/**
 * The model's sentences, asked for once the ranking is on screen. Keyed by the ranking itself, so
 * a new ranking (someone else assigned a job) asks again, and the same one never asks twice.
 */
export function useExplanations(jobId: number, ranking: Suggestion[] | undefined) {
  const signature = ranking?.map((s) => s.technicianId).join(",") ?? "";
  return useQuery({
    queryKey: ["suggestions", jobId, "explain", signature],
    queryFn: () => api<{ explanations: Explanation[]; model: string | null }>(`/jobs/${jobId}/suggestions/explain`, { method: "POST" }),
    enabled: signature.length > 0,
    staleTime: Infinity,
    retry: false,
  });
}

/** Assign through the dispatch API (which re-checks the licence and the window), and undo. */
export function useAssign(jobId: number) {
  const client = useQueryClient();
  const refresh = () => {
    client.invalidateQueries({ queryKey: ["jobs"] });
    // The ranking only: the same three technicians keep the sentences already written about them.
    client.invalidateQueries({ queryKey: ["suggestions", jobId], exact: true });
    client.invalidateQueries({ queryKey: ["dispatch"] });
  };

  const assign = useMutation({
    mutationFn: (technicianId: number) => api("/dispatch/assign", { method: "POST", body: JSON.stringify({ jobId, technicianId }) }),
    onSettled: refresh,
  });
  const unassign = useMutation({
    mutationFn: () => api("/dispatch/unassign", { method: "POST", body: JSON.stringify({ jobId }) }),
    onSettled: refresh,
  });
  return { assign, unassign };
}
