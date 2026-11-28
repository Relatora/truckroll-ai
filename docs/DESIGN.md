# Truckroll: Design and Build Plan

> Status: **Approved** 2026-11-25. **Built** as v0.1.0 on 2026-11-27. The plan below is kept
> as written; section 10 lists what changed on the way.

## 1. What it is

Truckroll is a CRM for a home-services company that runs heating, cooling and plumbing crews
out of several branches. Office staff book jobs, dispatchers route technicians, and an AI
layer does the work that eats their day: finding what was installed at a house, picking the
right technician for a job, and turning a phone call into a ticket and a follow-up.

The demo company is **Kestrel Home Comfort**, made up for this repo (the name is a nod to
Kestrel, the ASP.NET Core web server). It has five branches, about a dozen technicians, about
300 customers with their homes and equipment, two years of job history, equipment manuals,
warranty terms and company policies. Every record is generated from a fixed seed. Email
domains end in `.test`, and equipment brands are invented.

**Who it is for:** hiring managers for a hands-on technology leadership role at a growing,
multi-location home-services company with its own CRM. The repo has to show three things:

1. Strong hands-on C# / ASP.NET Core and TypeScript / Next.js on the stack such a company runs:
   EF Core, MySQL, Redis, SignalR, Docker.
2. Practical, safe AI in the places that matter to the business: customer service,
   dispatch and administration. Not a chat window bolted on the side.
3. How a technology lead runs a codebase: decisions written down, measured AI, tests, a
   review standard, an AI-usage policy and a roadmap the business can read.

**Hard rule: $0 to run.** Everything runs locally on one machine. Claude can be switched on
for better answers, but nothing depends on it.

## 2. Stack

| Concern        | Choice                                                    | Notes                                                                                                                |
| -------------- | --------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| Runtime        | .NET 10 (LTS)                                             | Not .NET 11, which is a two-year release. A CRM outlives an STS (ADR-001)                                            |
| API            | ASP.NET Core 10, minimal APIs, feature folders            | One deployable, split by feature. Job events go through an outbox to Kafka (ADR-005, ADR-009)                       |
| Data           | EF Core 10 + MySQL 8.4 LTS                                | Provider: `Microting.EntityFrameworkCore.MySql`, the maintained continuation of Pomelo (ADR-002)                     |
| Retrieval      | Redis 8 Query Engine via NRedisStack                      | MySQL Community has no vector search. Redis 8 does, and it's already the cache (ADR-003)                             |
| Real time      | SignalR                                                   | The dispatch board. Assistant answers stream over server-sent events                                                 |
| AI client      | Microsoft.Extensions.AI (`IChatClient`, `IEmbeddingGenerator`) | Ollama through OllamaSharp by default. Claude through the official `Anthropic` SDK when a key is set (ADR-004) |
| Models         | `qwen2.5:7b` chat, `nomic-embed-text` embeddings (768 dimensions) | Both on the RTX 3070. Embeddings always stay local                                                          |
| MCP            | `ModelContextProtocol.AspNetCore` (official C# SDK)       | Streamable HTTP at `/mcp`, personal access tokens                                                                    |
| Web            | Next.js 16, React 19, TypeScript, Tailwind 4, shadcn/ui   | Motion for animation, MapLibre with OpenFreeMap tiles, TanStack Query, cmdk                                          |
| Orchestration  | Aspire 17                                                 | One command starts MySQL, Redis, the API and the web app, with an OpenTelemetry dashboard (ADR-008)                  |
| Quality        | xUnit, Testcontainers, Playwright, `dotnet format`, ESLint, `tsc` | One local gate script. GitHub Actions is locked on this account, so the gate runs locally                     |

## 3. Domain model

```
Branch ──< Technician (skills, certifications, home base)
   │
   ├──< Customer ──< Property (address, lat/lng) ──< Equipment (kind, brand, model, serial, installed, warranty)
   │       └── Membership (Comfort Club plan, renews on)
   │
   ├──< Job (number, type, priority, status, window, required skill/cert) >── Technician
   │       └──< JobNote
   │
   └──< CallRecord (transcript → summary, proposed job, draft follow-up; approved by a person)

KnowledgeDocument (manual | warranty | policy, code like M-CAL-80 or P-104)
AppUser (role: owner | branch manager | dispatcher | CSR | technician, branch)
PersonalAccessToken (hashed, for MCP)
```

**Access rules.** An owner sees every branch. A branch manager, dispatcher or CSR sees their
own branch. A technician sees their own jobs and the customers on them. These rules live in
one place, the Access layer, and every path goes through it: API endpoints, retrieval filters
and MCP tools. The assistant can never show someone a record they couldn't open in the app.

## 4. AI features

### 4.1 Ask Truckroll (cited assistant)

**Indexing.** A background indexer turns manuals, policies, equipment records and job notes
into passages: documents are split along their headings, records become one passage each.
Phone numbers and emails are masked before anything is embedded. Each passage goes into one
Redis index with three kinds of fields: a 768-dimension vector (HNSW), the text (BM25), and
tags for identifiers (serial numbers, model numbers, job numbers) plus the branch it belongs to.

**Retrieval.** Three rankings run against that index, each filtered to what the person may see:
vector similarity, keyword, and exact identifier match. They are fused with weighted
Reciprocal Rank Fusion. Showroom AI taught me to give identifiers their own ranking: when an
exact serial number is in the question, keyword search finds it and vector search out-votes it.

**Generation.** The prompt holds numbered passages and must cite them as `[1]`, `[2]`, and say
it doesn't know when the passages don't answer the question. Answers stream to the browser.
Each citation opens the passage it came from.

### 4.2 Dispatch suggestions

For an unassigned job, the API ranks technicians in plain C#:

1. **Hard filter:** the technician holds the certification the job needs (a gas licence for a
   furnace, a refrigeration licence for an AC) and works in the job's branch or a neighbour.
2. **Score:** skill match, straight-line distance from where they'll be before the window,
   how loaded their day is, and whether they can make the promised window.

The model gets the top three with their scores and writes one sentence on each. It never
changes the order. The dispatcher confirms the pick, and the board updates for everyone.

### 4.3 Calls to tickets

A CSR pastes or uploads a call transcript. The model returns structured output: a summary,
the customer it matches, a proposed job (type, priority, equipment, symptoms) and a drafted
follow-up email. Nothing is created or sent until a person approves it, and the app has no
email sending at all: approved drafts land in an outbox view.

### 4.4 MCP server

Each person creates a personal access token on the MCP Access page and pastes the generated
config into Claude Desktop. The server exposes read tools (`search_customers`,
`get_customer_history`, `list_jobs`, `get_schedule`, `suggest_technicians`,
`ask_knowledge_base`) and one write tool that creates a draft, never a sent message. Every
tool runs as the token's owner through the same Access layer.

## 5. Screens

| Screen          | What it does                                                                              |
| --------------- | ----------------------------------------------------------------------------------------- |
| Landing         | The public product story, light and dark                                                  |
| Dispatch board  | A map of trucks and jobs and a lane per technician. Drag to assign, live for everyone      |
| Customer 360    | Property, equipment, warranty, membership and a service timeline                          |
| Job             | Details, notes and the dispatch suggestion panel                                          |
| Ask Truckroll   | Streaming answers with citations that open the source                                     |
| Calls           | Transcript in, summary, proposed job and draft out, approve or dismiss                    |
| Owner dashboard | Jobs, first-time-fix rate, revenue and membership renewals across branches                |
| MCP access      | Create and revoke tokens, copy a Claude Desktop config                                    |

Plus a ⌘K command palette, light and dark themes, and toasts. The look is polished and calm:
something an owner would be proud to show a customer, not a template.

## 6. Proving it works

- **Tests.** xUnit against real MySQL and Redis in Testcontainers. Access rules are tested from
  both sides on every path (web, retrieval, MCP). Tests never call a real model; a fake
  `IChatClient` and a deterministic fake embedder stand in.
- **Evals.** `tools/Truckroll.Eval` scores three things and writes `evals/results.md`:
  retrieval (about 50 questions with known answers: hit@1, hit@5, MRR), dispatch (scenarios
  with an expected technician: top-1 and top-3 accuracy) and call extraction (field accuracy
  over labelled transcripts).
- **End to end.** Playwright walks the demo as each role. A capture spec regenerates every
  README screenshot and the demo video, so they never go stale.

## 7. Engineering-leadership artifacts

`docs/adr/` (one decision per file), `AI_WORKFLOW.md` (how the team uses AI, and what never
goes into a prompt), `docs/CODE_REVIEW.md`, `docs/ONBOARDING.md`, `docs/DEMO.md`,
`docs/MCP.md`, `CONTRIBUTING.md` and `ROADMAP.md`. Every change goes issue → branch → PR,
with the design discussion on the issue.

## 8. Milestones

| Release                  | Theme                                                                                       | Done when                                                    |
| ------------------------ | ------------------------------------------------------------------------------------------- | ------------------------------------------------------------ |
| v0.1.0 · Demo slice      | The CRM screens and all four AI features, measured and documented                           | The two-minute demo runs end to end and the evals clear their bars |
| v0.2.0 · Event-driven    | Transactional outbox to Kafka, a notifications consumer, OpenTelemetry into Datadog         | Job events flow through Kafka with no lost or doubled events |
| v0.3.0 · Technician app  | React Native (Expo) app for technicians, with offline sync                                  | A tech can close a job with no signal and it syncs later     |
| v0.4.0 · Hosted          | Container images, Kubernetes manifests (Kustomize), Argo CD, a public demo on DigitalOcean  | A reviewer can log in to a live demo                         |

**v0.1 bars:** hybrid retrieval hit@5 ≥ 0.95 and at least as good as vector alone on hit@1;
the expected technician in the top three for at least 90% of dispatch scenarios; at least
90% field accuracy on call extraction.

## 9. Prerequisites and risks

- **.NET 10 SDK, Docker, Node 22, Ollama** with `qwen2.5:7b` and `nomic-embed-text`.
- **Memory.** The development machine has 15 GB and little headroom. Ollama runs on the GPU,
  dev servers get restarted between batches, and production builds never run alongside
  `next dev`.
- **A 7B model.** Good at reading a passage and citing it, weak at arithmetic and ranking.
  That's why dispatch ranking is code and the model only explains.
- **Provider churn.** Pomelo stopped at EF Core 9. If the Microting provider falls behind,
  Oracle's `MySql.EntityFrameworkCore` is the fallback (ADR-002).

## 10. What changed while building it

The plan held. These are the places where building it taught me something, with where each
one is recorded.

- **The model says words, code computes values.** qwen2.5:7b got weekday dates wrong, so call
  analysis returns "Wednesday" or "today", and C# turns that into a date. The same goes for
  customer matching: code matches by name and street; the model only reads the call.
- **Temperature 0 for anything evaluated.** At the default temperature, the same call could
  come back read differently from one run to the next, and an eval that moves on its own
  measures nothing.
- **Structured output needs the expected items named.** Asked for a list of explanations
  without being told which technicians to explain, the small model returned an empty list.
  The prompt now names the ids.
- **Redis needs its search module loaded explicitly under Aspire.** Aspire skips the image's
  entrypoint, so `WithModule(RedisModules.Search)` is in the AppHost. Testcontainers tests
  couldn't catch it, which is why AI work also gets checked on the real stack.
- **Booking runs at READ COMMITTED.** The job-number retry (MAX + 1, then retry on a
  duplicate) kept failing under MySQL's default REPEATABLE READ, because a retry inside the
  same transaction reads the same old snapshot and picks the same number.
- **The MCP server has seven tools, not six plus "a write tool".** The write tool is
  `create_draft_note`, and it creates a draft job note, never a message to a customer.
- **Drag and drop needed a real browser to test.** The Playwright suite found that a real mouse
  couldn't drag a job on the board, which no unit test or keyboard walkthrough showed (#18).
- **ADR-005 was written with the code,** as planned: in-process `JobChanged` events now, an
  outbox and Kafka in v0.2.
