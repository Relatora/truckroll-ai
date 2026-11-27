import type { Metadata } from "next";
import { Suspense } from "react";
import { CallsView } from "./calls-view";

export const metadata: Metadata = { title: "Calls" };

export default function Page() {
  // The view reads ?tab and ?call, which needs a Suspense boundary under cacheComponents.
  return (
    <Suspense>
      <CallsView />
    </Suspense>
  );
}
