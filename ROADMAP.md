# Roadmap

v0.1.0 was a working demo of the whole idea: the CRM screens a home-services company runs on,
and AI in the four places it saves time. v0.2.0 made job events reliable and observable. This is what comes next, written the way I'd plan a
quarter with a team: outcomes first, then the work, then how we'd know it worked. Each theme
is a GitHub milestone with its own issues.

## Where we are (v0.2.0 · Event-driven)

| Area           | What shipped                                                                                                          |
| -------------- | --------------------------------------------------------------------------------------------------------------------- |
| Platform       | .NET 10 + Aspire 17, EF Core 10 on MySQL 8.4, Redis 8, Next.js 16, one local quality gate, eight ADRs                  |
| CRM            | Five roles scoped by branch, Customer 360, jobs, a live dispatch board, an owner dashboard                              |
| Assistant      | Hybrid retrieval (vectors, words and identifiers) with access scoping; streamed, cited answers                          |
| Dispatch       | Ranking in code (licence, skill, timing, distance, workload), with the model explaining the top three                  |
| Calls          | A transcript becomes a proposed job and a drafted follow-up, approved by a person; an outbox instead of sending         |
| MCP            | Seven tools for Claude Desktop, personal tokens, one draft-only write                                                   |
| Events         | Every job change commits with its event through a transactional outbox; a relay sends them to one Kafka topic, read by the board, the search index and a notifications drafter |
| Observability  | OpenTelemetry to the Aspire dashboard and, with a key, the Datadog Agent; model calls carry provider, model and tokens; a committed Datadog dashboard |
| Proof          | 243 xUnit tests on real MySQL, Redis and Kafka, three evals with bars, 11 Playwright tests, regenerated screenshots and video, ten ADRs |

## Shipped in v0.2.0 · Event-driven

The plan was an outbox, Kafka, a notifications consumer and Datadog. All four shipped
([ADR-009](docs/adr/009-one-topic-at-least-once.md), [ADR-010](docs/adr/010-opentelemetry-to-datadog-agent.md)), with three changes:

- **One topic, `truckroll.jobs`, keyed by job,** instead of a topic per event type. Per-type
  topics can't keep one job's events in order.
- **The proof was a broker outage, not an API kill.** With Kafka paused, an assignment still
  succeeded, its event waited in MySQL, and it went out once Kafka was back, with one draft
  and no duplicates. Killing the API between the save and the publish can't lose an event by
  construction, because both commit in one transaction. Tests cover the resend after a failed
  send.
- **The Datadog dashboard is checked against the code, not yet against Datadog.** A test fails
  if it reads a metric the API doesn't export. Importing it into a real account is a v0.4 step.

## Next quarter

### 1. v0.3.0 · Technician app (highest priority)

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

### 2. v0.4.0 · Hosted

**Outcome:** a reviewer can sign in to a live demo without installing anything.

- Container images for the API and the web app, built and scanned in CI.
- Kubernetes manifests with Kustomize overlays, deployed by Argo CD to DigitalOcean.
- Managed MySQL and Redis; Ollama replaced by Claude for chat (embeddings stay on a small
  model in the cluster), switched by the existing setting.
- Rate limits on the assistant and MCP, which v0.1 doesn't have because it runs on one
  person's machine.
- A lease on the outbox relay, so more than one API instance can run (today two relays would
  each send the same events).
- The Datadog Agent as a DaemonSet, the committed dashboard imported and checked against real
  data, and the two charts v0.2 left out: time to dispatch and failed index updates.
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
