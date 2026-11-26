import type { Metadata } from "next";
import { Suspense } from "react";
import { CustomerView } from "./customer-view";

export const metadata: Metadata = { title: "Customer" };

/** The id comes from the URL on the client (useParams), inside a Suspense boundary. */
export default function CustomerPage() {
  return (
    <Suspense>
      <CustomerView />
    </Suspense>
  );
}
