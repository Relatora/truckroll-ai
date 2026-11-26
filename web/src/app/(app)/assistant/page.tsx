import type { Metadata } from "next";
import { NotBuiltYet } from "@/components/shell/not-built-yet";

export const metadata: Metadata = { title: "Ask Truckroll" };

export default function Page() {
  return <NotBuiltYet title="Ask Truckroll" description="Questions about equipment, history and policy, answered with sources you can open." issue={13} />;
}
