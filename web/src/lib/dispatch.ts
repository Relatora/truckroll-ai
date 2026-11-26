"use client";

import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from "@microsoft/signalr";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { api } from "./api";
import type { EquipmentKind, JobPriority, JobStatus, JobType } from "./customers";

/** Mirrors the records in Truckroll.Api/Features/Dispatch/DispatchEndpoints.cs. */

export type BoardTechnician = { id: number; name: string; color: string; skills: string; certifications: string; visiting: boolean };

export type BoardJob = {
  id: number;
  number: string;
  type: JobType;
  priority: JobPriority;
  status: JobStatus;
  windowStart: string;
  windowEnd: string;
  estimatedMinutes: number;
  summary: string;
  customer: string;
  street: string;
  latitude: number;
  longitude: number;
  equipmentKind: EquipmentKind | null;
  requiredCertification: string;
  technicianId: number | null;
};

export type Board = {
  date: string;
  branch: { id: number; code: string; name: string; latitude: number; longitude: number };
  canDispatch: boolean;
  technicians: BoardTechnician[];
  jobs: BoardJob[];
};

/** A broadcast from the hub: who changed which job, and how. */
export type JobChanged = { jobId: number; number: string; branchId: number; status: JobStatus; technicianId: number | null; change: string; changedBy: string; at: string };

const boardKey = (date: string, branch: string) => ["dispatch", "board", date, branch];

export function useBoard(date: string, branch: string) {
  const params = new URLSearchParams({ date });
  if (branch) params.set("branch", branch);
  return useQuery({ queryKey: boardKey(date, branch), queryFn: () => api<Board>(`/dispatch/board?${params}`) });
}

/**
 * Whether a technician holds the licence a job needs. The server has the final say; the board uses
 * this to show, while you drag, which lanes will accept a job.
 */
export function holdsLicence(technician: BoardTechnician, required: string) {
  if (required === "None") return true;
  return technician.certifications.split(",").map((c) => c.trim()).includes(required);
}

/**
 * Assign or unassign with an optimistic update: the block moves the instant it's dropped, and
 * slides back if the server refuses (no licence, already booked), with the server's reason.
 */
export function useMoveJob(date: string, branch: string) {
  const client = useQueryClient();
  const key = boardKey(date, branch);

  return useMutation({
    mutationFn: ({ jobId, technicianId }: { jobId: number; technicianId: number | null }) =>
      technicianId === null
        ? api<BoardJob>("/dispatch/unassign", { method: "POST", body: JSON.stringify({ jobId }) })
        : api<BoardJob>("/dispatch/assign", { method: "POST", body: JSON.stringify({ jobId, technicianId }) }),
    onMutate: async ({ jobId, technicianId }) => {
      await client.cancelQueries({ queryKey: key });
      const previous = client.getQueryData<Board>(key);
      client.setQueryData<Board>(key, (board) =>
        board
          ? {
              ...board,
              jobs: board.jobs.map((job) =>
                job.id === jobId ? { ...job, technicianId, status: technicianId === null ? "Scheduled" : "Dispatched" } : job,
              ),
            }
          : board,
      );
      return { previous };
    },
    onError: (_error, _variables, context) => {
      if (context?.previous) client.setQueryData(key, context.previous);
    },
    onSettled: () => {
      client.invalidateQueries({ queryKey: key });
      client.invalidateQueries({ queryKey: ["jobs"] });
    },
  });
}

export type LiveState = "connecting" | "live" | "reconnecting" | "offline";

/**
 * Joins the branch's channel on the dispatch hub and refreshes the board whenever anyone changes
 * a job there. Returns the connection state for the "Live" indicator.
 */
export function useDispatchHub(branchCode: string | undefined, onChange: (change: JobChanged) => void) {
  const client = useQueryClient();
  const [state, setState] = useState<LiveState>("connecting");
  const handler = useRef(onChange);
  useEffect(() => {
    handler.current = onChange;
  });

  useEffect(() => {
    if (!branchCode) return;
    let disposed = false;
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl("/hubs/dispatch")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("jobChanged", (change: JobChanged) => {
      client.invalidateQueries({ queryKey: ["dispatch", "board"] });
      handler.current(change);
    });
    connection.onreconnecting(() => setState("reconnecting"));
    connection.onreconnected(async () => {
      await connection.invoke("JoinBranch", branchCode);
      setState("live");
    });
    connection.onclose(() => !disposed && setState("offline"));

    connection
      .start()
      .then(() => connection.invoke("JoinBranch", branchCode))
      .then(() => !disposed && setState("live"))
      .catch(() => !disposed && setState("offline"));

    return () => {
      disposed = true;
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop();
    };
  }, [branchCode, client]);

  return state;
}
