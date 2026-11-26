import type { Metadata } from "next";
import { Suspense } from "react";
import { DispatchView } from "./dispatch-view";

export const metadata: Metadata = { title: "Dispatch board" };

export default function DispatchPage() {
  return (
    <Suspense>
      <DispatchView />
    </Suspense>
  );
}
