import type { Metadata } from "next";
import { NotBuiltYet } from "@/components/shell/not-built-yet";

export const metadata: Metadata = { title: "Jobs" };

export default function Page() {
  return <NotBuiltYet title="Jobs" description="Book, find and follow every job from the first call to the invoice." issue={8} />;
}
