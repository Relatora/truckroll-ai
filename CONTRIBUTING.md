# Contributing to Truckroll

This is how work moves through the repo. It's the process I'd run on a team of five or six
engineers plus a couple of contractors, applied to a smaller codebase. Contractors follow
the same flow as everyone else; that's how the work they hand over stays maintainable.

## The flow

1. **Every change starts as an issue.** It uses the template: the problem, the proposal,
   a "done when" checklist and what's out of scope. If it's too big for one PR, split it.
2. **Talk on the issue, not in chat.** Before starting, leave a short design note on the
   issue. When it merges, leave a note on what changed from the plan and why. Six months
   later, the issue is where someone looks.
3. **Branch from `main`** using `type/<issue>-short-name`, for example `feat/9-dispatch-board`,
   `fix/31-window-overlap`, `docs/19-readme`. Types: `feat`, `fix`, `chore`, `docs`, `test`.
4. **Commit in small steps.** One idea per commit. The subject is imperative ("Add dispatch
   board"), under about 70 characters, then a body that explains _why_ when it isn't obvious.
5. **Open a PR early** and fill in every heading of the template. Link the issue with
   `Closes #N`.
6. **The quality gate must pass** (see below) before anything merges.
7. **Squash-merge**, so `main` reads as one commit per change, then delete the branch.

Nothing goes to `main` without a PR.

## The quality gate

GitHub Actions is locked on this account, so the gate runs locally and the PR says so. One
command runs it:

```powershell
./scripts/ci.ps1          # Windows
./scripts/ci.sh           # macOS, Linux, Git Bash
```

The steps are listed in the script's header, and the README's quality-gate section is kept
in step with it. Each step has one job: style never gets discussed in review, the compiler
catches what it can, tests cover behaviour, and the web build proves the bundle still builds.

To run it before every push:

```bash
git config core.hooksPath .githooks
```

## Keep PRs reviewable

Aim for a PR someone can review properly in 20 minutes: roughly 400 changed lines or fewer,
not counting lock files, migrations, generated files and screenshots. If it's bigger, split it.
Land the model and the migration first, then the endpoint, then the screen, then the polish.

## Tests

- Integration tests for anything a person or an AI client can do, against real MySQL and
  Redis in Testcontainers. Unit tests for pure logic (chunking, rank fusion, PII masking,
  dispatch scoring).
- **Never call a real model in tests.** Use the fake chat client and the deterministic fake
  embedder, so tests are fast and give the same answer every time.
- Access rules get tested from both sides: the person who should get in and the one who
  shouldn't, through every path (API, retrieval, MCP).

## Using AI assistants on this codebase

Read [`AI_WORKFLOW.md`](AI_WORKFLOW.md) first. The short version: you own every line you
commit, AI output gets the same review as anyone else's, and customer data never goes into a
prompt.

## Writing things down

If you make a decision that someone will later question ("why is it done this way?"),
write an ADR in [`docs/adr`](docs/adr) from the template. It takes ten minutes and saves a
meeting.
