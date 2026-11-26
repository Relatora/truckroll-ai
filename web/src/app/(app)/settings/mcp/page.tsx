import type { Metadata } from "next";
import { NotBuiltYet } from "@/components/shell/not-built-yet";

export const metadata: Metadata = { title: "MCP access" };

export default function Page() {
  return <NotBuiltYet title="MCP access" description="Connect Claude Desktop to Truckroll with a personal token." issue={16} />;
}
