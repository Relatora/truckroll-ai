"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { LogOut, MoonStar, Search, Sun, UserRound } from "lucide-react";
import { useTheme } from "next-themes";
import { useRouter } from "next/navigation";
import { useDeferredValue, useEffect, useState, useSyncExternalStore } from "react";
import {
  Command,
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandShortcut,
} from "@/components/ui/command";
import { api } from "@/lib/api";
import { signOut, useMe } from "@/lib/session";
import { cn } from "@/lib/utils";
import { navigationFor, toneStyles } from "./navigation";

/** Mirrors CustomerSearchResult in Truckroll.Api/Features/Customers/CustomerEndpoints.cs. */
type CustomerHit = { id: number; name: string; address: string | null; branchCode: string };

/**
 * ⌘K (Ctrl+K on Windows): jump to any page, find a customer by name, phone or street, or switch
 * the theme. Customer results come from the API, so they respect the same access rules as every
 * other screen.
 */
export function CommandMenu() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { resolvedTheme, setTheme } = useTheme();
  const { data: me } = useMe();

  // ⌘ on a Mac, Ctrl elsewhere. Read after hydration so server and client markup agree.
  const isMac = useSyncExternalStore(
    () => () => {},
    () => /Mac|iPhone|iPad/.test(navigator.platform),
    () => false,
  );

  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const term = useDeferredValue(search.trim());

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key.toLowerCase() === "k" && (event.metaKey || event.ctrlKey)) {
        event.preventDefault();
        setOpen((value) => !value);
      }
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  const customers = useQuery({
    queryKey: ["customers", "search", term],
    queryFn: () => api<CustomerHit[]>(`/customers/search?q=${encodeURIComponent(term)}`),
    enabled: open && term.length >= 2,
    staleTime: 60_000,
  });

  function go(href: string) {
    setOpen(false);
    setSearch("");
    router.push(href);
  }

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="glass group flex h-11 w-full max-w-xl items-center gap-3 rounded-full pr-2 pl-4 text-sm text-muted-foreground transition-[transform,box-shadow] duration-300 ease-[var(--ease-spring)] hover:-translate-y-0.5 hover:shadow-lift focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
      >
        <Search className="size-4 transition-transform duration-300 group-hover:scale-110 group-hover:-rotate-12" />
        <span className="flex-1 truncate text-left">
          <span className="sm:hidden">Search</span>
          <span className="hidden sm:inline">Find a customer, page or setting</span>
        </span>
        <kbd className="hidden rounded-full bg-foreground/[0.06] px-2.5 py-1 text-xs font-semibold text-foreground/70 sm:inline">
          {isMac ? "⌘K" : "Ctrl K"}
        </kbd>
      </button>

      <CommandDialog
        open={open}
        onOpenChange={setOpen}
        title="Find anything"
        description="Search customers and pages"
        className="glass-strong sm:max-w-xl"
      >
        <Command>
          <CommandInput value={search} onValueChange={setSearch} placeholder="Name, phone, street or page…" />
          <CommandList>
            <CommandEmpty>
              {term.length < 2 ? "Type at least two letters." : customers.isFetching ? "Searching…" : "Nothing matches that."}
            </CommandEmpty>

            {customers.data && customers.data.length > 0 && (
              <CommandGroup heading="Customers">
                {customers.data.map((customer) => (
                  <CommandItem
                    key={customer.id}
                    // cmdk filters on value; include what was searched so server matches survive.
                    value={`customer ${customer.name} ${customer.address ?? ""} ${term}`}
                    onSelect={() => go(`/customers/${customer.id}`)}
                  >
                    <UserRound />
                    <span>{customer.name}</span>
                    <span className="truncate text-muted-foreground">{customer.address}</span>
                    <CommandShortcut>{customer.branchCode}</CommandShortcut>
                  </CommandItem>
                ))}
              </CommandGroup>
            )}

            <CommandGroup heading="Pages">
              {navigationFor(me).map((item) => (
                <CommandItem
                  key={item.href}
                  value={`page ${item.label} ${item.keywords?.join(" ") ?? ""}`}
                  onSelect={() => go(item.href)}
                >
                  <span className={cn("flex size-7 items-center justify-center rounded-lg", toneStyles[item.tone])}>
                    <item.icon className="size-4" />
                  </span>
                  <span>{item.label}</span>
                  <span className="truncate text-muted-foreground">{item.blurb}</span>
                </CommandItem>
              ))}
            </CommandGroup>

            <CommandGroup heading="Settings">
              <CommandItem
                value="theme dark light mode"
                onSelect={() => setTheme(resolvedTheme === "dark" ? "light" : "dark")}
              >
                {resolvedTheme === "dark" ? <Sun /> : <MoonStar />}
                <span>{resolvedTheme === "dark" ? "Use light theme" : "Use dark theme"}</span>
              </CommandItem>
              <CommandItem
                value="sign out log out"
                onSelect={async () => {
                  setOpen(false);
                  await signOut();
                  queryClient.clear();
                  router.replace("/sign-in");
                }}
              >
                <LogOut />
                <span>Sign out</span>
              </CommandItem>
            </CommandGroup>
          </CommandList>
        </Command>
      </CommandDialog>
    </>
  );
}
