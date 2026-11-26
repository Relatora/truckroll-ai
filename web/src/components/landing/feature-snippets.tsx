import { CircleCheck, Clock3, Phone } from "lucide-react";

/**
 * Static renderings of what the three AI features produce, using the demo company's real stories.
 * Each sits in the thermal frame that marks model-written content in the app itself.
 */

export function AnswerSnippet() {
  return (
    <div className="ai-drafted rounded-xl bg-card p-5 text-sm shadow-[0_20px_50px_-30px] shadow-foreground/40">
      <p className="font-medium">What did we install at 14 Maple St, and has it given trouble?</p>
      <div className="mt-4 space-y-3 leading-relaxed text-card-foreground/90">
        <p>
          A Calderon CAL-80-100 furnace and a Frostline FL-16 air conditioner, both installed in October 2020
          <Cite n={1} />. The furnace&apos;s hot surface ignitor has cracked twice, most recently last month
          <Cite n={2} />.
        </p>
        <p>
          Its serial number falls in the range of service bulletin SB-2024-07, which covers the ignitor, a revised
          bracket and an hour of labour <Cite n={3} />.
        </p>
      </div>
      <ol className="mt-4 space-y-1 border-t pt-3 text-xs text-muted-foreground">
        <li>1. Equipment at 14 Maple St, Guelph</li>
        <li>2. Job note by Nadia Kowalczyk</li>
        <li>3. SB-2024-07, CAL-80 ignitor cracking</li>
      </ol>
    </div>
  );
}

function Cite({ n }: { n: number }) {
  return (
    <sup className="ml-0.5 inline-flex size-4 items-center justify-center rounded bg-frost-soft text-[0.65rem] font-semibold text-frost">
      {n}
    </sup>
  );
}

const CANDIDATES = [
  { name: "Nadia Kowalczyk", reason: "G2 licence, furnace specialist, finishing 3 km away at 11:30", fit: "Best fit" },
  { name: "Marco Bellini", reason: "G1 licence, but 28 km away in Kitchener", fit: "Possible" },
];

const EXCLUDED = [
  { name: "Tyler Fischer", reason: "Holds a G3, which can't attend a gas emergency alone" },
  { name: "Ben Okoro", reason: "No gas licence" },
];

export function DispatchSnippet() {
  return (
    <div className="rounded-xl bg-card p-5 text-sm shadow-[0_20px_50px_-30px] shadow-foreground/40">
      <div className="flex items-start justify-between gap-4">
        <div>
          <p className="font-medium">No heat, 14 Maple St</p>
          <p className="text-muted-foreground">Calderon CAL-80 furnace, needs a G2</p>
        </div>
        <span className="rounded-full bg-ember px-2 py-0.5 text-xs font-semibold text-white">Emergency</span>
      </div>
      <ol className="ai-drafted mt-4 divide-y rounded-lg">
        {CANDIDATES.map((candidate, index) => (
          <li key={candidate.name} className="flex items-center gap-3 px-3 py-2.5">
            <span className="tabular w-4 text-muted-foreground">{index + 1}</span>
            <span className="flex-1">
              <span className="block font-medium">{candidate.name}</span>
              <span className="block text-muted-foreground">{candidate.reason}</span>
            </span>
            {index === 0 && <span className="text-xs font-semibold text-success">{candidate.fit}</span>}
          </li>
        ))}
      </ol>
      <ul className="mt-3 space-y-1 text-xs text-muted-foreground">
        {EXCLUDED.map((person) => (
          <li key={person.name}>
            <span className="font-medium text-foreground/80">{person.name}</span>: {person.reason}
          </li>
        ))}
      </ul>
      <div className="mt-4 flex justify-end">
        <span className="rounded-md bg-primary px-3 py-1.5 text-xs font-semibold text-primary-foreground">Assign Nadia</span>
      </div>
    </div>
  );
}

export function CallSnippet() {
  return (
    <div className="rounded-xl bg-card p-5 text-sm shadow-[0_20px_50px_-30px] shadow-foreground/40">
      <p className="flex items-center gap-2 text-muted-foreground">
        <Phone className="size-3.5" aria-hidden />
        Doug Hartley, 8:05 am, Kitchener
      </p>
      <blockquote className="mt-2 border-l-2 pl-3 text-muted-foreground italic">
        &ldquo;The pressure&apos;s dropping again… Third time this week. And there&apos;s a bit of water at the bottom of
        that pipe on the side.&rdquo;
      </blockquote>
      <div className="ai-drafted mt-4 space-y-3 rounded-lg p-3">
        <p>
          <span className="font-medium">Summary.</span> Boiler pressure keeps dropping and the customer is refilling it
          every other day. Water at the relief valve outlet.
        </p>
        <p className="flex items-center gap-2">
          <Clock3 className="size-3.5 text-muted-foreground" aria-hidden />
          Repair, Hearthmark HMB-110, Friday 8 to 10 am
        </p>
        <p className="text-muted-foreground">Follow-up drafted: &ldquo;Your boiler visit on Friday morning&rdquo;</p>
      </div>
      <p className="mt-3 flex items-center gap-2 text-xs text-muted-foreground">
        <CircleCheck className="size-3.5" aria-hidden />
        Nothing is booked or sent until a CSR approves it.
      </p>
    </div>
  );
}
