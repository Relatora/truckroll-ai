import { AppSidebar } from "@/components/shell/app-sidebar";
import { CommandMenu } from "@/components/shell/command-menu";
import { UserMenu } from "@/components/shell/user-menu";
import { SidebarInset, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";

/**
 * The signed-in app: the navy sidebar, a top bar with search (⌘K) and the account menu, and the
 * page. The proxy has already bounced anyone without a session cookie.
 */
export default function AppLayout({ children }: LayoutProps<"/">) {
  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset className="bg-background">
        <header className="sticky top-0 z-20 flex h-14 items-center gap-3 border-b bg-background/90 px-4 backdrop-blur supports-[backdrop-filter]:bg-background/75 md:px-6">
          <SidebarTrigger className="-ml-1" />
          <div className="flex flex-1 justify-center md:justify-start">
            <CommandMenu />
          </div>
          <UserMenu />
        </header>
        <div className="flex flex-1 flex-col px-4 py-6 md:px-8 md:py-8">{children}</div>
      </SidebarInset>
    </SidebarProvider>
  );
}
