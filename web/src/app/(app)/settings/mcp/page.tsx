import type { Metadata } from "next";
import { McpView } from "./mcp-view";

export const metadata: Metadata = { title: "MCP access" };

export default function Page() {
  return <McpView />;
}
