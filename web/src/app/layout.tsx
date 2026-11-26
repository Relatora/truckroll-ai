import type { Metadata } from "next";
import { Archivo } from "next/font/google";
import "./globals.css";
import { Providers } from "@/components/providers";

/**
 * One family at two widths: expanded and heavy for display (truck lettering), normal width for
 * the interface. The width axis is what makes a single family carry both voices.
 */
const archivo = Archivo({
  variable: "--font-archivo",
  subsets: ["latin"],
  axes: ["wdth"],
});

export const metadata: Metadata = {
  // Pages set their own name; the browser tab reads "Dispatch board – Truckroll".
  title: { default: "Truckroll", template: "%s – Truckroll" },
  description: "A CRM for heating, cooling and plumbing companies, with AI for dispatch, calls and equipment history.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    // next-themes sets the theme class before React hydrates, which React would otherwise flag.
    <html lang="en" className={`${archivo.variable} h-full antialiased`} suppressHydrationWarning>
      <body className="flex min-h-full flex-col">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
