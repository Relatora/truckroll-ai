import type { Metadata } from "next";
import { Suspense } from "react";
import { CustomersView } from "./customers-view";

export const metadata: Metadata = { title: "Customers" };

/** The filters live in the URL (?q=&membership=&sort=&page=), which needs a Suspense boundary. */
export default function CustomersPage() {
  return (
    <Suspense>
      <CustomersView />
    </Suspense>
  );
}
