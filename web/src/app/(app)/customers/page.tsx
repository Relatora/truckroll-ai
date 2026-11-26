import type { Metadata } from "next";
import { NotBuiltYet } from "@/components/shell/not-built-yet";

export const metadata: Metadata = { title: "Customers" };

export default function Page() {
  return <NotBuiltYet title="Customers" description="Every home you serve, what's installed there, and what went wrong before." issue={7} />;
}
