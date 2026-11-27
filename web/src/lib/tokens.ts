"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";

/** Mirrors Truckroll.Api/Features/Tokens/TokenEndpoints.cs. */

export type TokenItem = {
  id: number;
  name: string;
  prefix: string;
  createdAt: string;
  expiresAt: string;
  lastUsedAt: string | null;
  revokedAt: string | null;
  live: boolean;
};

export type CreatedToken = { item: TokenItem; token: string };

export function useTokens() {
  return useQuery({ queryKey: ["tokens"], queryFn: () => api<TokenItem[]>("/tokens") });
}

export function useCreateToken() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (name: string) => api<CreatedToken>("/tokens", { method: "POST", body: JSON.stringify({ name }) }),
    onSettled: () => client.invalidateQueries({ queryKey: ["tokens"] }),
  });
}

export function useRevokeToken() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => api<TokenItem>(`/tokens/${id}/revoke`, { method: "POST" }),
    onSettled: () => client.invalidateQueries({ queryKey: ["tokens"] }),
  });
}

/**
 * Claude Desktop's config for Truckroll. Claude Desktop talks to local programs over stdio, so
 * mcp-remote bridges it to /mcp. The token goes in an environment variable, not the arguments:
 * a space in "Bearer trk_…" inside args breaks on Windows.
 */
export function claudeDesktopConfig(origin: string, token: string) {
  return JSON.stringify(
    {
      mcpServers: {
        truckroll: {
          command: "npx",
          args: ["-y", "mcp-remote", `${origin}/mcp`, "--header", "Authorization:${TRUCKROLL_AUTH}"],
          env: { TRUCKROLL_AUTH: `Bearer ${token}` },
        },
      },
    },
    null,
    2,
  );
}
