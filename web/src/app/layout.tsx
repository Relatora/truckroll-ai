import type { Metadata } from "next";
import { Bricolage_Grotesque, Figtree } from "next/font/google";
import "./globals.css";
import { Providers } from "@/components/providers";

/**
 * Bricolage Grotesque for display: its optical-size axis draws big headlines with tight,
 * characterful detail that a text face can't. Figtree for the interface: friendly, clear at
 * 13px, with proper tabular figures for times and money.
 */
const bricolage = Bricolage_Grotesque({
  variable: "--font-bricolage",
  subsets: ["latin"],
  axes: ["opsz", "wdth"],
});

const figtree = Figtree({
  variable: "--font-figtree",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  // Pages set their own name; the browser tab reads "Dispatch board – Truckroll".
  title: { default: "Truckroll", template: "%s – Truckroll" },
  description: "A CRM for heating, cooling and plumbing companies, with AI for dispatch, calls and equipment history.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    // next-themes sets the theme class before React hydrates, which React would otherwise flag.
    <html lang="en" className={`${bricolage.variable} ${figtree.variable} h-full antialiased`} suppressHydrationWarning>
      <body className="flex min-h-full flex-col">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
