import { Bot, Database, Gauge, Layers, ShieldCheck, Workflow } from "lucide-react";
import Link from "next/link";
import { Aurora } from "@/components/brand/aurora";
import { Wordmark } from "@/components/brand/wordmark";
import { FeatureStory } from "@/components/landing/feature-story";
import { Headline } from "@/components/landing/headline";
import { HeroStage } from "@/components/landing/hero-stage";
import { SpotlightCard } from "@/components/motion/spotlight-card";
import { Reveal, RevealItem } from "@/components/motion/reveal";
import { ThemeSwitch } from "@/components/motion/theme-switch";
import { Button } from "@/components/ui/button";

const REPO = "https://github.com/Relatora/truckroll-ai";

const STACK = [
  { icon: Layers, title: "C# and ASP.NET Core 10", body: "Minimal APIs, Entity Framework Core and SignalR, built the way a growing shop's own CRM would be.", wide: true },
  { icon: Database, title: "MySQL and Redis", body: "MySQL 8.4 keeps the records. Redis 8 is both the cache and the search index." },
  { icon: Bot, title: "Models you choose", body: "Local models through Ollama, or Claude, behind one interface. Swapping is a setting." },
  { icon: ShieldCheck, title: "One set of access rules", body: "Screens, search and AI clients all go through the same rules, so the assistant can't leak a record.", wide: true },
  { icon: Workflow, title: "MCP built in", body: "Claude Desktop can work inside Truckroll, as the person who connected it." },
  { icon: Gauge, title: "Observable", body: "Aspire and OpenTelemetry today. Kafka, Kubernetes and Datadog on the roadmap." },
];

/** The public front door: Truckroll's story, told with the demo company's own data. */
export default function LandingPage() {
  return (
    <div className="relative flex flex-1 flex-col overflow-x-clip">
      <Aurora />

      <header className="fixed inset-x-0 top-4 z-50 px-4">
        <nav className="glass mx-auto flex max-w-5xl items-center gap-2 rounded-full py-2 pr-2 pl-5">
          <Link href="/" className="mr-auto rounded-lg text-lg outline-none focus-visible:ring-3 focus-visible:ring-ring/50">
            <Wordmark />
          </Link>
          <a href="#features" className="hidden rounded-full px-3 py-2 text-sm font-medium text-foreground/75 transition-colors hover:bg-foreground/[0.06] hover:text-foreground sm:block">
            Features
          </a>
          <a href="#stack" className="hidden rounded-full px-3 py-2 text-sm font-medium text-foreground/75 transition-colors hover:bg-foreground/[0.06] hover:text-foreground sm:block">
            Stack
          </a>
          <a href={REPO} className="hidden rounded-full px-3 py-2 text-sm font-medium text-foreground/75 transition-colors hover:bg-foreground/[0.06] hover:text-foreground sm:block">
            Code
          </a>
          <ThemeSwitch className="size-9" />
          <Button asChild size="lg" className="rounded-full px-4">
            <Link href="/sign-in">Sign in</Link>
          </Button>
        </nav>
      </header>

      <section className="mx-auto w-full max-w-6xl px-6 pt-40 md:pt-48">
        <div className="mx-auto max-w-4xl text-center">
          <Reveal>
            <RevealItem>
              <p className="glass mx-auto mb-8 inline-flex items-center gap-2 rounded-full px-4 py-1.5 text-sm font-medium">
                <span className="size-2 rounded-full bg-[image:var(--thermal)]" />
                A CRM for heating, cooling and plumbing companies
              </p>
            </RevealItem>
          </Reveal>
          <Headline lines={["Know the house", "before the truck pulls up."]} className="type-display text-[clamp(2.6rem,7vw,5.6rem)]" />
          <Reveal>
            <RevealItem>
              <p className="mx-auto mt-7 max-w-2xl text-lg leading-relaxed text-muted-foreground md:text-xl">
                Truckroll books the call, routes the right technician, and keeps every furnace&apos;s history one question away.
              </p>
            </RevealItem>
            <RevealItem className="mt-10 flex flex-wrap justify-center gap-3">
              <Button asChild variant="thermal" size="xl">
                <Link href="/sign-in">Open the demo</Link>
              </Button>
              <Button asChild size="xl" variant="ghost" className="glass hover:-translate-y-0.5">
                <a href={REPO}>Read the code</a>
              </Button>
            </RevealItem>
          </Reveal>
        </div>

        <div className="mt-20 md:mt-28">
          <HeroStage />
        </div>
      </section>

      <section id="features" aria-labelledby="ai-heading" className="mx-auto w-full max-w-6xl scroll-mt-24 px-6 pt-28 md:pt-36">
        <Reveal inView className="max-w-2xl">
          <RevealItem>
            <h2 id="ai-heading" className="type-display text-4xl md:text-6xl">
              AI where the office day actually goes
            </h2>
          </RevealItem>
          <RevealItem>
            <p className="mt-5 text-lg text-muted-foreground">
              Anything a model wrote wears the shimmering outline. A person always decides what happens next.
            </p>
          </RevealItem>
        </Reveal>
        <div className="mt-8">
          <FeatureStory />
        </div>
      </section>

      <section id="stack" aria-labelledby="stack-heading" className="mx-auto w-full max-w-6xl scroll-mt-24 px-6 pt-24 md:pt-32">
        <Reveal inView className="max-w-2xl">
          <RevealItem>
            <h2 id="stack-heading" className="type-display text-4xl md:text-5xl">
              Built on the stack a growing shop runs
            </h2>
          </RevealItem>
          <RevealItem>
            <p className="mt-4 text-lg text-muted-foreground">
              Every decision is written down in the repository, with the trade-offs.{" "}
              <a href={`${REPO}/tree/main/docs/adr`} className="font-medium text-foreground underline decoration-foreground/30 underline-offset-4 hover:decoration-foreground">
                Read the decisions
              </a>
            </p>
          </RevealItem>
        </Reveal>
        <Reveal inView className="mt-12 grid gap-4 md:grid-cols-3">
          {STACK.map((item) => (
            <RevealItem key={item.title} className={item.wide ? "md:col-span-2" : undefined}>
              <SpotlightCard className="group h-full p-6">
                <span className="flex size-11 items-center justify-center rounded-xl bg-foreground/[0.06] transition-transform duration-300 ease-[var(--ease-spring)] group-hover:-rotate-6 group-hover:scale-110">
                  <item.icon className="size-5" />
                </span>
                <h3 className="mt-6 font-display text-xl font-semibold tracking-tight">{item.title}</h3>
                <p className="mt-2 text-muted-foreground">{item.body}</p>
              </SpotlightCard>
            </RevealItem>
          ))}
        </Reveal>
      </section>

      <section className="mx-auto w-full max-w-6xl px-6 py-32">
        <Reveal inView>
          <RevealItem>
            <div className="glass-strong relative overflow-hidden rounded-[2rem] px-8 py-16 text-center md:py-24">
              <div aria-hidden className="absolute -top-1/2 left-1/2 size-[36rem] -translate-x-1/2 rounded-full bg-[image:var(--thermal)] opacity-25 blur-[100px]" />
              <h2 className="type-display relative text-4xl md:text-6xl">See Guelph&apos;s morning for yourself</h2>
              <p className="relative mx-auto mt-5 max-w-xl text-lg text-muted-foreground">
                Sign in as the owner, a dispatcher, a customer service rep or a technician. Each one sees a different Truckroll.
              </p>
              <div className="relative mt-10 flex justify-center">
                <Button asChild variant="thermal" size="xl">
                  <Link href="/sign-in">Open the demo</Link>
                </Button>
              </div>
            </div>
          </RevealItem>
        </Reveal>
      </section>

      <footer className="border-t border-foreground/[0.07]">
        <div className="mx-auto flex w-full max-w-6xl flex-wrap justify-between gap-4 px-6 py-8 text-sm text-muted-foreground">
          <p>Kestrel Home Comfort, its customers and its technicians are invented. So is every address.</p>
          <p>
            A portfolio project by Amr Abdallah.{" "}
            <a href={REPO} className="font-medium text-foreground underline-offset-4 hover:underline">
              Source on GitHub
            </a>
          </p>
        </div>
      </footer>
    </div>
  );
}
