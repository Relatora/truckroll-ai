# Roadmap

v0.1.0 is a working demo of the whole idea: the CRM screens a home-services company runs on,
and AI in the four places it saves time. This is what comes next, written the way I'd plan a
quarter with a team: outcomes first, then the work, then how we'd know it worked. Each theme
is a GitHub milestone with its own issues.

## Where we are (v0.1.0 · Demo slice)

| Area           | What shipped                                                                                                          |
| -------------- | --------------------------------------------------------------------------------------------------------------------- |
| Platform       | .NET 10 + Aspire 17, EF Core 10 on MySQL 8.4, Redis 8, Next.js 16, one local quality gate, eight ADRs                  |
| CRM            | Five roles scoped by branch, Customer 360, jobs, a live dispatch board, an owner dashboard                              |
| Assistant      | Hybrid retrieval (vectors, words and identifiers) with access scoping; streamed, cited answers                          |
| Dispatch       | Ranking in code (licence, skill, timing, distance, workload), with the model explaining the top three                  |
| Calls          | A transcript becomes a proposed job and a drafted follow-up, approved by a person; an outbox instead of sending         |
| MCP            | Seven tools for Claude Desktop, personal tokens, one draft-only write                                                   |
| Proof          | 223 xUnit tests on real MySQL and Redis, three evals with bars, 11 Playwright tests, regenerated screenshots and video |

## Next quarter

### 1. v0.2.0 · Event-driven (highest priority)

**Outcome:** other systems hear about every job change, reliably, and someone can see how the
system is behaving.

- A transactional outbox: the job save and its event commit together ([ADR-005](docs/adr/005-modular-monolith-then-kafka.md)).
- A relay to Kafka topics `job.created`, `job.assigned` and `job.completed`, keyed by job id.
- A notifications consumer, the first thing outside the API: "your technician is on the way"
  as drafts first, behind the same approval rule as follow-ups.
- The dispatch hub and the search index move from in-process handlers to Kafka consumers.
- OpenTelemetry already flows to the Aspire dashboard. Send it to Datadog too, with a
  dashboard for the numbers an owner cares about: time to dispatch, model latency, failed
  index updates.
- **We'd know it worked when:** a chaos test that kills the API between the save and the
  publish loses no events and doubles none.

### 2. v0.3.0 · Technician app

**Outcome:** a technician runs their day from their phone, including in a basement with no
signal.

- React Native (Expo), with the same API and a token scheme for devices.
- Today's jobs, directions, the equipment history at the address, notes and photos.
- Offline first: changes queue on the device and sync when the signal returns, with clear
  conflict rules (the office wins on assignment, the technician wins on notes).
- The assistant in the field: "what's the error code on a Calderon CAL-80?" answered from the
  manuals, cited.
- **We'd know it worked when:** a technician can close a job in airplane mode and it shows up
  on the dispatcher's board within a minute of reconnecting.

### 3. v0.4.0 · Hosted

**Outcome:** a reviewer can sign in to a live demo without installing anything.

- Container images for the API and the web app, built and scanned in CI.
- Kubernetes manifests with Kustomize overlays, deployed by Argo CD to DigitalOcean.
- Managed MySQL and Redis; Ollama replaced by Claude for chat (embeddings stay on a small
  model in the cluster), switched by the existing setting.
- Rate limits on the assistant and MCP, which v0.1 doesn't have because it runs on one
  person's machine.
- **We'd know it worked when:** a fresh cluster comes up from the repo with one command, and
  the demo resets itself every night.

## Later, not yet

These are good ideas that aren't worth doing before the work above.

- **Sending email and SMS for real.** Follow-ups are drafts on purpose. Sending needs consent
  tracking, unsubscribe handling and a provider, and that's a project of its own.
- **Telephony integration** that brings calls in as transcripts automatically, instead of a
  CSR pasting them.
- **Route optimisation** across a whole day. Today's ranking picks the best technician for one
  job; planning the whole day is a different problem, and it should start with a solver rather
  than a model.
- **Fine-tuning a model** on trade language. The evals say retrieval and prompting are where
  the gains are, so this waits until they say otherwise.

## How we'd run it

- Two-week iterations. Each starts from this roadmap and ends with a release and notes.
- One theme at a time owns most of the capacity. About 20% stays free for bugs and
  maintenance.
- Every theme has its "we'd know it worked when" line. If we can't measure it, we write down
  why we're doing it anyway.
