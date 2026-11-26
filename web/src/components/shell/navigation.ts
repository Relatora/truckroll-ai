import {
  Bot,
  KeyRound,
  LayoutDashboard,
  PhoneIncoming,
  Route,
  Users,
  Wrench,
  type LucideIcon,
} from "lucide-react";
import type { Me } from "@/lib/session";

export type NavItem = {
  href: string;
  label: string;
  icon: LucideIcon;
  /** Shown only to people who can use the page. The API enforces the same rule independently. */
  visibleTo: (me: Me) => boolean;
  /** Words the command palette should also match, beyond the label. */
  keywords?: string[];
};

const everyone = () => true;

/**
 * The app's pages, in the order a dispatcher's day touches them. Navigation, the command palette
 * and page titles all read from this list, so a page is named the same everywhere.
 */
export const navigation: NavItem[] = [
  { href: "/dashboard", label: "Overview", icon: LayoutDashboard, visibleTo: everyone, keywords: ["home", "kpi"] },
  { href: "/dispatch", label: "Dispatch board", icon: Route, visibleTo: everyone, keywords: ["schedule", "map", "trucks"] },
  { href: "/calls", label: "Calls", icon: PhoneIncoming, visibleTo: (me) => me.can.canReviewCalls, keywords: ["inbox", "transcripts"] },
  { href: "/customers", label: "Customers", icon: Users, visibleTo: everyone, keywords: ["homes", "equipment"] },
  { href: "/jobs", label: "Jobs", icon: Wrench, visibleTo: everyone, keywords: ["work orders", "tickets"] },
  { href: "/assistant", label: "Ask Truckroll", icon: Bot, visibleTo: everyone, keywords: ["ai", "assistant", "search"] },
  { href: "/settings/mcp", label: "MCP access", icon: KeyRound, visibleTo: everyone, keywords: ["claude", "tokens", "api"] },
];

export function navigationFor(me: Me | undefined) {
  return me ? navigation.filter((item) => item.visibleTo(me)) : [];
}
