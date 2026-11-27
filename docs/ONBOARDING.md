# Onboarding

Your first day on Truckroll. By the end of it you should have the stack running, know where
things live, and have a first change open as a PR. Read [DESIGN.md](DESIGN.md) first: it's the
plan, and it's short.

## Day one: get it running

You need:

- the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker Desktop
- Node 22 or newer
- [Ollama](https://ollama.com)

A GPU helps but isn't required. Setup takes about fifteen minutes, most of it downloading
images and models.

```bash
ollama pull qwen2.5:7b
ollama pull nomic-embed-text
cd web && npm install && cd ..
dotnet run --project src/Truckroll.AppHost --launch-profile http
```

Aspire starts MySQL, Redis, the API and the web app, and prints a link to its dashboard. Open
the dashboard first. Every resource, its logs and its traces are there, and it's where you'll
look when something misbehaves.

On first start the API migrates and seeds the database (Kestrel Home Comfort, for today's date),
then builds the search index in the background (about 40 seconds once the embedding model is
loaded). The web app is at http://localhost:3000. Sign in with one of the role cards. The
password for all of them is `truckroll-demo`.

| Login                | Role           | Sees                                    |
| -------------------- | -------------- | --------------------------------------- |
| `morgan@kestrel.test` | Owner          | All five branches, and the dashboard    |
| `pat@kestrel.test`    | Branch manager | Kitchener, and its numbers              |
| `alex@kestrel.test`   | Dispatcher     | Guelph's board, jobs and calls          |
| `dana@kestrel.test`   | CSR            | Guelph's customers and calls            |
| `nadia@kestrel.test`  | Technician     | Her own jobs and those customers        |

Then check the models: `GET http://localhost:3000/api/dev/ai-check` asks the chat model one
question and embeds one string, and tells you which models answered.

### When something goes wrong

These are the ones I actually hit.

| Symptom                                                | Cause and fix                                                                                                                                               |
| ------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| The board shows yesterday, or jobs look stale          | The demo is seeded for the day it was created, and the database volume survives restarts. Press **Reset demo data** on the `api` resource in the dashboard. |
| The assistant cites nothing                            | The index is empty or still building. `GET /api/dev/index` says whether it's ready; **Rebuild knowledge index** on the `api` resource rebuilds it.           |
| Search fails with an unknown command `FT.CREATE`       | Redis started without its search module. The AppHost loads it with `WithModule(RedisModules.Search)`; don't remove that.                                      |
| `ai-check` returns 503                                 | Ollama isn't running, or a model isn't pulled. `ollama list` shows what's there.                                                                             |
| Tests fail with Docker containers exiting (code 255)   | The machine ran out of memory. Stop loaded models (`ollama stop qwen2.5:7b`) and close the stack before running the gate.                                    |
| `scripts/ci.ps1` refuses the web build                 | A `next dev` for this app is running. The gate won't build next to it; stop the stack first.                                                                 |
| `dotnet format` fails on a file you didn't touch much  | Your editor saved it with CRLF line endings. The repo is LF.                                                                                                 |
| A certificate trust prompt on first start              | Aspire may ask once to trust its local development certificate. It's a system prompt, so it's yours to answer; nothing in the repo answers it for you.     |

## Day one: read the code in this order

The order follows one request from the outside in. Each file is short and commented for this.

1. **`src/Truckroll.AppHost/AppHost.cs`**: what runs, and how the pieces find each other.
2. **`src/Truckroll.Data/Domain/`**: branches, customers, properties, equipment, technicians,
   jobs and calls. The data model is the business.
3. **`src/Truckroll.Api/Access/AccessScope.cs`**: **the most important file.** Every read goes
   through it, and it decides who sees what. `AccessConventionTests` fails the build if feature
   code reads a table directly.
4. **`src/Truckroll.Api/Features/Jobs/`**: a plain feature: endpoints, validation and the
   `JobChanged` event it publishes after a save (ADR-005).
5. **`src/Truckroll.Api/Features/Dispatch/`**: the SignalR hub that keeps every board live, and
   `Suggestions/DispatchScorer.cs`, the pure ranking code (ADR-007).
6. **`src/Truckroll.Api/Knowledge/`**: masking (`PiiMasker`), passages, the Redis index and
   `KnowledgeSearch`, which fuses three rankings (ADR-003).
7. **`src/Truckroll.Api/Features/Assistant/`** and **`Features/Calls/`**: where a model is
   actually called, and the guard rails around it.
8. **`src/Truckroll.Api/Features/Mcp/TruckrollTools.cs`**: the same features, exposed to Claude
   Desktop as the token's owner ([MCP.md](MCP.md)).
9. **`web/src/lib/`** then **`web/src/app/(app)/`**: one hook file per feature, then the pages
   that use them. The API is same-origin, so there are no tokens in the browser (ADR-006).
10. **`tests/Truckroll.Api.Tests/Infrastructure/`**: how tests get a real MySQL and Redis, and a
    fake model whose replies a test scripts.

## Where things live

```
src/Truckroll.AppHost/          Aspire: what runs locally, and the dashboard buttons
src/Truckroll.ServiceDefaults/  OpenTelemetry, health checks, resilience
src/Truckroll.Data/             EF Core model, migrations, the deterministic demo seed
src/Truckroll.Api/
  Access/                       AccessScope, roles, personal tokens
  Ai/                           Model wiring: Ollama or Claude, behind Microsoft.Extensions.AI
  Knowledge/                    Masking, passages, the Redis index, hybrid search
  Features/<Name>/              One folder per feature: endpoints and the code only it uses
tests/Truckroll.Api.Tests/      xUnit v3 + Testcontainers; fake models, real databases
tools/Truckroll.Eval/           Retrieval, dispatch and extraction evals → evals/*.md
web/                            Next.js 16: app/ pages, components/, lib/ hooks, e2e/ Playwright
docs/                           Design, ADRs, MCP, this file, demo script, review guide
```

## Your first ticket

A good first change touches every layer once, without touching the AI:

> **Show a job's equipment warranty on the dispatch board's job card.**
> The board's job card shows the customer and window. Add "under warranty until …" when the
> job's equipment is still covered, so a dispatcher knows before sending a tech.

You'll add one field to the board query in `Features/Dispatch/DispatchEndpoints.cs` (through
`AccessScope`, like every read), extend the `BoardJob` type in `web/src/lib/dispatch.ts`, show it
in `JobCard` in `dispatch-view.tsx`, and cover it with a test in
`tests/Truckroll.Api.Tests/Features/DispatchTests.cs`. Then:

1. Open an issue from the template, and write a short design note on it.
2. Branch `feat/<issue>-short-name`, commit, and run `./scripts/ci.ps1` until it passes.
3. Open the PR with the template, and say what you checked in the browser.

[CONTRIBUTING.md](../CONTRIBUTING.md) has the conventions, and [CODE_REVIEW.md](CODE_REVIEW.md)
says what review will look for.

## Who to ask

Ask on the issue, not in a DM, so the next person finds the answer. If the answer turns out to
be "that's how we decided to do it", check that an ADR says so, and if none does, that's a
good second PR.
