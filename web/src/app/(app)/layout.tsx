import { Suspense } from "react";
import { Aurora } from "@/components/brand/aurora";
import { ThemeSwitch } from "@/components/motion/theme-switch";
import { CommandMenu } from "@/components/shell/command-menu";
import { Dock } from "@/components/shell/dock";
import { UserMenu } from "@/components/shell/user-menu";

/**
 * The signed-in workspace. No sidebar: a floating dock for navigation, a floating command bar as
 * the fastest way anywhere, and the page given the full width under a calm aurora.
 */
export default function AppLayout({ children }: LayoutProps<"/">) {
  return (
    <>
      <Aurora calm />
      {/* The dock reads the current path, which only exists at request time on dynamic routes. */}
      <Suspense>
        <Dock />
      </Suspense>
      <div className="flex min-h-svh flex-1 flex-col md:pl-24">
        <header className="sticky top-0 z-30 flex items-center gap-3 px-4 pt-4 pb-2 md:px-8">
          <div className="flex flex-1 justify-center">
            <CommandMenu />
          </div>
          <ThemeSwitch />
          <UserMenu />
        </header>
        <main className="flex flex-1 flex-col px-4 pt-6 pb-28 md:px-8 md:pb-12">{children}</main>
      </div>
    </>
  );
}
