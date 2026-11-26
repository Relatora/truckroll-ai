import type { Metadata } from "next";
import { NotBuiltYet } from "@/components/shell/not-built-yet";

export const metadata: Metadata = { title: "Dispatch board" };

export default function Page() {
  return <NotBuiltYet title="Dispatch board" description="Today's trucks on the map and every technician's day, live for everyone on the board." issue={9} />;
}
