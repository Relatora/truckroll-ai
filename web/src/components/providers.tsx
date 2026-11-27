"use client";

import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MotionConfig } from "motion/react";
import { ThemeProvider } from "next-themes";
import { useState } from "react";
import { Toaster } from "@/components/ui/sonner";
import { TooltipProvider } from "@/components/ui/tooltip";
import { ApiError } from "@/lib/api";

/** Client-side providers for the whole app: theme, server state, tooltips and toasts. */
export function Providers({ children }: { children: React.ReactNode }) {
  // One client per browser session, created lazily so the server never shares one between users.
  const [queryClient] = useState(() => {
    // Any 401 means the session ended (expired, signed out in another tab, or the account is
    // gone): send them to sign in, then back to where they were. Once, and as a full page load,
    // which also throws away everything cached for that user. Clearing the cache in place made
    // every mounted query refetch, each 401 restarted the navigation, and it never finished.
    let leaving = false;
    const onError = (error: unknown) => {
      if (error instanceof ApiError && error.status === 401 && window.location.pathname !== "/sign-in" && !leaving) {
        leaving = true;
        const next = encodeURIComponent(window.location.pathname + window.location.search);
        window.location.replace(`/sign-in?next=${next}`);
      }
    };

    const client = new QueryClient({
      queryCache: new QueryCache({ onError }),
      mutationCache: new MutationCache({ onError }),
      defaultOptions: { queries: { staleTime: 30_000, refetchOnWindowFocus: false } },
    });
    return client;
  });

  return (
    <ThemeProvider attribute="class" defaultTheme="system" enableSystem disableTransitionOnChange>
      {/* One spring for the whole app, so every motion feels like the same material. Motion
          turns transforms into instant changes for people who ask for reduced motion. */}
      <MotionConfig reducedMotion="user" transition={{ type: "spring", stiffness: 420, damping: 32, mass: 0.8 }}>
        <QueryClientProvider client={queryClient}>
          <TooltipProvider delayDuration={200}>
            {children}
            {/* On a phone the dock sits along the bottom; toasts (and their Undo) go above it. */}
            <Toaster position="bottom-right" mobileOffset={{ bottom: 88 }} />
          </TooltipProvider>
        </QueryClientProvider>
      </MotionConfig>
    </ThemeProvider>
  );
}
