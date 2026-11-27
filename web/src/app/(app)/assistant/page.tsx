import type { Metadata } from "next";
import { AssistantView } from "./assistant-view";

export const metadata: Metadata = { title: "Ask Truckroll" };

export default function Page() {
  return <AssistantView />;
}
