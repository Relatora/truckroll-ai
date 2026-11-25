"use client";

import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError } from "@/lib/api";

/** Client-side providers for the whole app: server state through TanStack Query. */
export function Providers({ children }: { children: React.ReactNode }) {
  const router = useRouter();

  // One client per browser session, created lazily so the server never shares one between users.
  const [queryClient] = useState(() => {
    // Any 401 means the session ended (expired, or signed out in another tab): clear what we
    // cached for that user and send them to sign in, then back to where they were.
    const onError = (error: unknown) => {
      if (error instanceof ApiError && error.status === 401 && window.location.pathname !== "/sign-in") {
        client.clear();
        const next = encodeURIComponent(window.location.pathname + window.location.search);
        router.replace(`/sign-in?next=${next}`);
      }
    };

    const client = new QueryClient({
      queryCache: new QueryCache({ onError }),
      mutationCache: new MutationCache({ onError }),
      defaultOptions: { queries: { staleTime: 30_000, refetchOnWindowFocus: false } },
    });
    return client;
  });

  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}
