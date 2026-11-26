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

/** The colour a section wears: its icon tile, its glow, its accents. */
export type Tone = "frost" | "ember" | "aqua" | "violet" | "ink";

export type NavItem = {
  href: string;
  label: string;
  /** One line on what the page is for, shown on overview tiles. */
  blurb: string;
  icon: LucideIcon;
  tone: Tone;
  /** Shown only to people who can use the page. The API enforces the same rule independently. */
  visibleTo: (me: Me) => boolean;
  /** Words the command palette should also match, beyond the label. */
  keywords?: string[];
};

const everyone = () => true;

/**
 * The app's pages, in the order a dispatcher's day touches them. The dock, the command palette
 * and the overview tiles all read from this list, so a page is named the same everywhere.
 */
export const navigation: NavItem[] = [
  { href: "/dashboard", label: "Overview", blurb: "Your day at a glance", icon: LayoutDashboard, tone: "ink", visibleTo: everyone, keywords: ["home", "kpi"] },
  { href: "/dispatch", label: "Dispatch board", blurb: "Trucks, lanes and the map, live", icon: Route, tone: "frost", visibleTo: everyone, keywords: ["schedule", "map", "trucks"] },
  { href: "/calls", label: "Calls", blurb: "This morning's calls, ready to approve", icon: PhoneIncoming, tone: "ember", visibleTo: (me) => me.can.canReviewCalls, keywords: ["inbox", "transcripts"] },
  { href: "/customers", label: "Customers", blurb: "Homes, equipment and history", icon: Users, tone: "aqua", visibleTo: everyone, keywords: ["homes", "equipment"] },
  { href: "/jobs", label: "Jobs", blurb: "Book, find and follow work", icon: Wrench, tone: "violet", visibleTo: everyone, keywords: ["work orders", "tickets"] },
  { href: "/assistant", label: "Ask Truckroll", blurb: "Answers with sources you can open", icon: Bot, tone: "violet", visibleTo: everyone, keywords: ["ai", "assistant", "search"] },
  { href: "/settings/mcp", label: "MCP access", blurb: "Connect Claude Desktop", icon: KeyRound, tone: "ink", visibleTo: everyone, keywords: ["claude", "tokens", "api"] },
];

export function navigationFor(me: Me | undefined) {
  return me ? navigation.filter((item) => item.visibleTo(me)) : [];
}

/** Icon-tile colours per tone, shared by the dock, tiles and palette. */
export const toneStyles: Record<Tone, string> = {
  frost: "bg-frost-soft text-frost",
  ember: "bg-ember-soft text-ember",
  aqua: "bg-aqua-soft text-aqua",
  violet: "bg-violet-soft text-violet",
  ink: "bg-foreground/[0.07] text-foreground",
};
