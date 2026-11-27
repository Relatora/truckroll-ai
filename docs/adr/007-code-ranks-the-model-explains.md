# ADR-007: Code ranks dispatch suggestions; the model only explains them

- **Status:** Accepted
- **Date:** 2026-11-27

## Context

Choosing who goes to a job is the dispatcher's most frequent decision, and an expensive one to get
wrong. Send someone without the licence and it's illegal. Send someone across the county and it's
an unpaid hour on the road. Send someone already booked and a customer waits.

A language model could be asked to "pick the best technician". It would often pick well, and it
would explain itself fluently. But:

- **Its order can't be audited.** "Why Nadia and not Ben?" needs an answer a branch manager can
  check, and the same question must get the same answer tomorrow.
- **A hard rule must never be traded off.** A model weighs everything, so a G3 who is very close
  could be talked into a G2 job.
- **The arithmetic is easy and the model is bad at it.** Kilometres, minutes late and jobs in a
  day are what decide this, and a 7B model is unreliable at that maths. showroom-ai learned the
  same lesson with counting: let code do the numbers and the model do the words.

## Decision

- `DispatchScorer.Rank` is a pure function of the job, the candidates and their day. It has no
  database, clock or model inside, so the same input always gives the same order.
- **Licences and clashes are filters.** A ruled-out technician is listed with the reason.
- **Four parts are weighted and published as constants:**
  - skill match 0.30
  - window fit 0.30, including whether this job would make them late for their next one
  - distance from where they'll be coming from 0.25
  - workload 0.15
- Every score carries its facts (km, from where, free from, arrival, minutes late, jobs that day),
  and a rules-built sentence is made from those facts.
- The model gets the top three's facts and returns one sentence each as structured output. Its
  answer is mapped back onto the code's order by technician id. Unknown ids are dropped, and a
  missing or unusable sentence falls back to the rules sentence. It can't add, drop or reorder.
- **Assigning stays a person's click.** The click goes through the same endpoint the board uses,
  which re-checks the licence and the window and broadcasts the change.
- `evals/dispatch.json` holds scenarios with a dispatcher's expected answer, written before the
  scorer was run on them. The bar is the expected technician in the top three in at least 90% of
  scenarios.

## Consequences

- A disagreement with a suggestion is a conversation about weights, not about prompts. The weights
  are four numbers in one file, with an eval to show what changing them does.
- Explanations arrive a few seconds after the ranking, because they're asked for separately. The
  page shows the rules sentence until the model's arrives, and keeps it when the model is down.
- Distance is straight-line, not road distance, and travel time is a flat 50 km/h plus 10
  minutes. That's good enough to rank within a branch. Route optimisation is out of scope.
- The first eval run scored top-3 1.00 and top-1 0.95. The one top-1 miss is a close call
  between a free technician 9 km away and a busier one across the road. It's recorded in
  `evals/dispatch-results.md` rather than tuned away.

## Alternatives considered

- **Let the model rank.** Fluent and flexible, but not repeatable, not auditable, and able to
  argue past a licence rule.
- **Let the model re-rank code's shortlist.** Still unrepeatable, for a small gain that the
  weights can deliver openly.
- **No explanation, just scores.** Accurate, but a dispatcher under pressure reads "4 km away,
  free by 10:30" faster than four bars, and the sentence is what makes the shortlist feel
  trustworthy.
