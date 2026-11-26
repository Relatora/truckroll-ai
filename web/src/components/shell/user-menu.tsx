"use client";

import { useQueryClient } from "@tanstack/react-query";
import { LogOut, Monitor, MoonStar, Sun } from "lucide-react";
import { useTheme } from "next-themes";
import { useRouter } from "next/navigation";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { roleLabels, signOut, useMe } from "@/lib/session";

/** The signed-in person: their name, role, theme choice and sign-out. */
export function UserMenu() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { theme, setTheme } = useTheme();
  const { data: me } = useMe();

  if (!me) {
    return <div className="size-9 rounded-full bg-muted" aria-hidden />;
  }

  const initials = me.displayName
    .split(" ")
    .map((part) => part[0])
    .join("")
    .slice(0, 2);

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        className="rounded-full bg-[image:var(--thermal)] p-[2px] transition-transform duration-200 ease-[var(--ease-spring)] hover:scale-105 active:scale-95 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
        aria-label={`Account menu for ${me.displayName}`}
      >
        <Avatar className="size-9 border-2 border-background">
          <AvatarFallback className="bg-primary text-sm font-semibold text-primary-foreground">{initials}</AvatarFallback>
        </Avatar>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="glass-strong w-64 rounded-xl">
        <DropdownMenuLabel className="font-normal">
          <p className="font-semibold">{me.displayName}</p>
          <p className="text-muted-foreground">
            {roleLabels[me.role]}, {me.branch ? me.branch.name : "all branches"}
          </p>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuLabel className="text-xs font-medium text-muted-foreground">Theme</DropdownMenuLabel>
        <DropdownMenuRadioGroup value={theme} onValueChange={setTheme}>
          <DropdownMenuRadioItem value="light">
            <Sun /> Light
          </DropdownMenuRadioItem>
          <DropdownMenuRadioItem value="dark">
            <MoonStar /> Dark
          </DropdownMenuRadioItem>
          <DropdownMenuRadioItem value="system">
            <Monitor /> Match my computer
          </DropdownMenuRadioItem>
        </DropdownMenuRadioGroup>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          onSelect={async () => {
            await signOut();
            queryClient.clear();
            router.replace("/sign-in");
          }}
        >
          <LogOut /> Sign out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
