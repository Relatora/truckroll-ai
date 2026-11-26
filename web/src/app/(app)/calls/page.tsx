import type { Metadata } from "next";
import { NotBuiltYet } from "@/components/shell/not-built-yet";

export const metadata: Metadata = { title: "Calls" };

export default function Page() {
  return <NotBuiltYet title="Calls" description="This morning's calls, summarised into tickets and follow-ups for you to approve." issue={15} />;
}
