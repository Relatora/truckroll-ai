import Link from "next/link";
import { Wordmark } from "@/components/brand/wordmark";
import { AnswerSnippet, CallSnippet, DispatchSnippet } from "@/components/landing/feature-snippets";
import { HeroBoard } from "@/components/landing/hero-board";
import { Button } from "@/components/ui/button";

const REPO = "https://github.com/Relatora/truckroll-ai";

const FEATURES = [
  {
    title: "Ask what's in the basement",
    body: "Staff ask in plain words and get an answer built from your own equipment records, job notes and manuals. Every claim links to where it came from, so nobody has to take the model's word for it.",
    snippet: <AnswerSnippet />,
  },
  {
    title: "Send the technician who's allowed to do the job",
    body: "Truckroll ranks technicians on licences, skills, distance and the rest of their day, in plain code you can audit. The model only explains the pick. The dispatcher makes the call.",
    snippet: <DispatchSnippet />,
  },
  {
    title: "Turn a phone call into a ticket",
    body: "Paste a call transcript and get a summary, a proposed job and a follow-up email for the customer. Nothing is booked or sent until someone on your team approves it.",
    snippet: <CallSnippet />,
  },
];

const STACK = [
  ["Back end", "C#, ASP.NET Core 10, Entity Framework Core, SignalR"],
  ["Data", "MySQL 8.4 for records, Redis 8 for search and cache"],
  ["Front end", "Next.js 16, React 19, TypeScript, Tailwind"],
  ["AI", "Local models through Ollama, or Claude, behind one interface"],
  ["Integrations", "An MCP server, so AI assistants work inside the same access rules as people"],
  ["Operations", "Aspire and OpenTelemetry today; Kafka, Kubernetes and Datadog on the roadmap"],
];

/** The public front door: what Truckroll does, shown with the demo company's own data. */
export default function LandingPage() {
  return (
    <div className="flex flex-1 flex-col">
      <section className="bg-[#14213d] pb-20 text-white dark:bg-[#0a1424] md:pb-24">
        <header className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-6 py-6">
          <Wordmark className="text-2xl" inverted />
          <nav className="flex items-center gap-1 text-sm">
            <a href={REPO} className="rounded-md px-3 py-2 text-slate-200 hover:bg-white/10 hover:text-white">
              Code
            </a>
            <Link href="/sign-in" className="rounded-md px-3 py-2 font-medium text-white hover:bg-white/10">
              Sign in
            </Link>
          </nav>
        </header>

        <div className="mx-auto max-w-6xl px-6 pt-10 md:pt-20">
          <h1 className="type-display max-w-4xl text-[clamp(2.6rem,7vw,5.4rem)]">Know the house before the truck pulls up.</h1>
          <p className="mt-6 max-w-2xl text-lg leading-relaxed text-slate-300">
            Truckroll is a CRM for heating, cooling and plumbing companies. It books the call, routes the right
            technician, and keeps every furnace&apos;s history one question away.
          </p>
          <div className="mt-9 flex flex-wrap gap-3">
            <Button asChild size="lg" className="bg-ember px-6 font-semibold text-white hover:bg-ember/90">
              <Link href="/sign-in">Open the demo</Link>
            </Button>
            <Button asChild size="lg" variant="ghost" className="px-6 text-white hover:bg-white/10 hover:text-white">
              <a href={REPO}>Read the code</a>
            </Button>
          </div>

          <div className="mt-16 md:mt-20">
            <HeroBoard />
          </div>
        </div>
      </section>

      <section aria-labelledby="ai-heading" className="mx-auto w-full max-w-6xl px-6 py-24 md:py-32">
        <h2 id="ai-heading" className="type-title max-w-2xl text-3xl md:text-4xl">
          AI where the office day actually goes
        </h2>
        <p className="mt-4 max-w-2xl text-lg text-muted-foreground">
          Anything a model wrote carries the warm-to-cool outline. A person decides what happens next.
        </p>

        <div className="mt-16 space-y-24">
          {FEATURES.map((feature, index) => (
            <article key={feature.title} className="grid items-center gap-10 md:grid-cols-2 md:gap-16">
              <div className={index % 2 === 1 ? "md:order-2" : undefined}>
                <h3 className="type-title text-2xl">{feature.title}</h3>
                <p className="mt-4 max-w-md leading-relaxed text-muted-foreground">{feature.body}</p>
              </div>
              <div className={index % 2 === 1 ? "md:order-1" : undefined}>{feature.snippet}</div>
            </article>
          ))}
        </div>
      </section>

      <section aria-labelledby="stack-heading" className="border-t bg-card">
        <div className="mx-auto grid w-full max-w-6xl gap-10 px-6 py-20 md:grid-cols-[1fr_2fr]">
          <div>
            <h2 id="stack-heading" className="type-title text-2xl">
              Built on the stack a growing shop runs
            </h2>
            <p className="mt-3 text-muted-foreground">
              Every decision is written down in the repository, with the trade-offs.
            </p>
            <a href={`${REPO}/tree/main/docs/adr`} className="mt-4 inline-block font-medium text-frost underline-offset-4 hover:underline">
              Architecture decisions
            </a>
          </div>
          <dl className="grid gap-x-8 gap-y-5 sm:grid-cols-2">
            {STACK.map(([term, detail]) => (
              <div key={term}>
                <dt className="text-sm font-semibold">{term}</dt>
                <dd className="mt-1 text-sm text-muted-foreground">{detail}</dd>
              </div>
            ))}
          </dl>
        </div>
      </section>

      <footer className="border-t">
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
