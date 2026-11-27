# Code review guide

What I look for in a Truckroll PR, roughly in this order. The order matters: a perfectly named
variable in a query that leaks another branch's customers is still a blocker.

## 1. Does it do the right thing?

- The PR says what problem it solves, and links the issue with the design note.
- It does what the issue asked, and only that. Unrelated clean-ups go in their own PR.
- It handles the boring cases: no technicians on the board, an empty transcript, a customer
  with no equipment, a job on a day the branch is closed.
- It holds up when two people act at once. Two dispatchers moving the same job, or two CSRs
  booking at the same moment, should end in one clear winner and a message for the other,
  never a duplicate job number.

## 2. Is it safe?

- **Every read goes through `AccessScope`.** Feature code never touches `db.<DbSet>` directly
  (`AccessConventionTests` fails the build if it does). New endpoints get a test from both
  sides: someone who may see the data does, and someone who may not gets a 404 or 403.
- **Nothing that changes data trusts the client** about who it is or which branch it's in.
  That comes from the signed-in person, never from the request body.
- **Model output is checked before it's used.** Ids are checked against what the model was
  given. Dates, priorities and matches are computed by code. Rules like "a gas smell is always
  an emergency" are enforced after the model, not asked of it.
- **No contact details reach a model.** Text goes through `PiiMasker` (or `PiiVault` when the
  real values must come back) before it's embedded or sent.
- **A person approves anything that leaves the building.** There's still no email sending, and
  an MCP tool that writes needs two reviewers and a reason ([AI_WORKFLOW.md](../AI_WORKFLOW.md)).
- **Secrets stay out of the repo** and out of logs. Personal tokens are logged by prefix only.

## 3. Will the next person understand it?

- Names say what things are in the trade's words: a job, a window, a lane, a licence, not
  `entity`, `slot` or `resource`.
- A comment says *why*, especially when the obvious approach was tried and failed ("REPEATABLE
  READ re-reads the same snapshot, so the retry never sees the new number").
- A decision that shapes the codebase gets an ADR in the same PR.
- The feature folder holds everything only that feature uses. Shared code earns its place in
  `Common/` by having a second user.

## 4. Is it tested?

- There's a test that fails without the change. For a bug fix, that test reproduces the bug.
- Tests use the real MySQL and Redis containers, not an in-memory stand-in, because the bugs
  that matter here (isolation levels, index syntax) don't show up in fakes.
- Tests never call a real model. AI features script the fake chat client and assert what the
  code does with the reply, including a bad or empty one.
- A change to prompts, ranking or retrieval comes with an eval run, and the PR shows the
  before and after numbers from `evals/*.md`.
- A change you can see in the browser says what was checked, in light and dark and at phone
  width. The Playwright suite covers the demo path.

## 5. Does it perform?

- No query in a loop. A list page is one query, or a fixed few.
- Lists are paged on the server.
- Nothing slow happens inside a request that a person is waiting on. Indexing goes on the
  knowledge queue; a model call streams or shows progress.
- Hot paths say how they were measured, not just that they're "fast".

## Tone

Review the code, not the person. Ask before asserting ("does this need the branch filter?").
Say which comments block the merge and which are suggestions. Approve once the blockers are
fixed. Don't hold a PR hostage for taste.
