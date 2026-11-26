"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { Wordmark } from "@/components/brand/wordmark";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarRail,
} from "@/components/ui/sidebar";
import { roleLabels, useMe } from "@/lib/session";
import { navigationFor } from "./navigation";

/** The ink-navy spine of the app: who you are, where you are, and every page you can use. */
export function AppSidebar() {
  const pathname = usePathname();
  const { data: me } = useMe();

  return (
    <Sidebar collapsible="icon">
      <SidebarHeader className="px-4 pt-5 pb-3 group-data-[collapsible=icon]:px-2">
        <Link href="/dashboard" className="text-sidebar-accent-foreground group-data-[collapsible=icon]:hidden">
          <Wordmark className="text-[1.35rem]" inverted />
        </Link>
        <Link
          href="/dashboard"
          aria-label="Truckroll overview"
          className="type-display hidden size-8 items-center justify-center rounded-md bg-sidebar-primary text-sm text-sidebar-primary-foreground group-data-[collapsible=icon]:flex"
        >
          T
        </Link>
      </SidebarHeader>

      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupContent>
            <SidebarMenu>
              {navigationFor(me).map((item) => {
                const active = pathname === item.href || pathname.startsWith(item.href + "/");
                return (
                  <SidebarMenuItem key={item.href}>
                    <SidebarMenuButton
                      asChild
                      isActive={active}
                      tooltip={item.label}
                      className="relative data-[active=true]:font-semibold data-[active=true]:before:absolute data-[active=true]:before:inset-y-1.5 data-[active=true]:before:-left-2 data-[active=true]:before:w-1 data-[active=true]:before:rounded-full data-[active=true]:before:bg-sidebar-primary"
                    >
                      <Link href={item.href} aria-current={active ? "page" : undefined}>
                        <item.icon />
                        <span>{item.label}</span>
                      </Link>
                    </SidebarMenuButton>
                  </SidebarMenuItem>
                );
              })}
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>
      </SidebarContent>

      <SidebarFooter className="px-4 pb-4 group-data-[collapsible=icon]:hidden">
        {me && (
          <div className="text-xs leading-relaxed text-sidebar-foreground/80">
            <p className="font-medium text-sidebar-accent-foreground">Kestrel Home Comfort</p>
            <p>{me.branch ? `${me.branch.name} branch` : "All five branches"}</p>
            <p>{roleLabels[me.role]}</p>
          </div>
        )}
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
  );
}
