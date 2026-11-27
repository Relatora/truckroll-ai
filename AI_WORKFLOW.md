# AI workflow

How a team should use AI on a codebase like this one: the coding assistants we work with, and
the AI features we ship to the office and the field. It's short on purpose. A policy nobody
remembers is no policy at all.

The one-line version: **AI makes us faster. It doesn't change who is accountable.** The person
who opens the PR owns every line in it, however it was written.

## 1. Coding with an assistant

Use one. Most of us are faster with an assistant for scaffolding, tests, refactors, reading
unfamiliar code and first drafts of docs. The rules are about keeping the quality bar where it
is.

**You review it before anyone else does.** If you couldn't explain a line in review, it
doesn't go in the PR. "The assistant wrote it" is not an answer to a review question.

**Tests are not optional, and they're not the assistant's to grade.** Generated code comes
with a test that fails without the change. Read generated tests closely: a common failure is a
test that asserts whatever the code happens to do, rather than what it should do.

**Watch for the usual failure modes:**

- **APIs that don't exist, or exist in another version.** .NET 10, EF Core 10 and Next.js 16
  all changed things an assistant's memory may not know: Aspire 17 made `AddProject<T>`
  obsolete, and assistants trained before that still suggest it. Check the installed package
  (the Next.js docs ship in `node_modules/next/dist/docs/`), not the assistant's recollection.
- **Access quietly dropped.** A query on `db.Customers` instead of `scope.Customers()` is the
  most expensive bug in this codebase. `AccessConventionTests` catches the obvious form, and
  reviewers check the rest on every PR.
- **Confident comments** that describe what the code should do rather than what it does.
- **Plausible but wrong domain details,** like a licence a furnace doesn't need, or a warranty
  rule nobody asked for.

**Keep PRs small.** It's easy to generate a 2,000-line PR. Nobody can review one. The size
guidance in [CONTRIBUTING.md](CONTRIBUTING.md) applies whatever wrote the code.

## 2. What never goes into a prompt

This applies to any assistant or model that isn't running on our own hardware.

- **Customer data:** names with contact details, phone numbers, emails, addresses, door and
  alarm codes, access notes, payment details, call recordings. Use the seeded demo data, or mask it first.
- **Secrets:** `appsettings.*.json` with real values, user secrets, API keys, tokens, database
  dumps, private keys.
- **Anything under NDA** or a supplier contract.

If you're not sure, it doesn't go in. Ask first.

## 3. AI features in the product

The same principles, built into the code so nobody has to remember them.

| Principle                              | How it's enforced                                                                                                                                                  |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| The model sees only what the user may  | Retrieval filters by branch and record inside the Redis query, through `AccessScope`. Every MCP tool runs as the token's owner                                     |
| Contact details stay out of prompts    | `PiiMasker` runs before text is embedded or sent to a model. Call analysis masks with `PiiVault` and puts the real values back only on the server, after the model |
| Code decides, the model explains       | Dispatch ranking is `DispatchScorer`, pure C#. The model writes one sentence per technician and can't reorder them ([ADR-007](docs/adr/007-code-ranks-the-model-explains.md)) |
| The model says words, code computes    | Call analysis returns "Wednesday" or "today". C# turns that into a date, matches the customer and picks the equipment                                               |
| Safety rules aren't the model's call   | A gas smell or CO alarm is raised to Emergency by code (policy P-103), whatever the model said                                                                     |
| Answers show their work                | Every answer cites numbered passages that open in a drawer. "The sources don't say" is an acceptable answer                                                         |
| Invented facts are thrown away         | Explanations for technicians the model wasn't asked about are dropped. Call proposals are validated before anyone sees them                                         |
| Model output is data, not code         | Rendered as text, never as HTML. Never executed                                                                                                                    |
| A person presses the button            | Calls stay proposals until a CSR approves them. Follow-ups go to an outbox; Truckroll has no way to send email. MCP can only add a labelled draft note              |
| Quality is measured                    | `tools/Truckroll.Eval` scores retrieval, dispatch ranking and call extraction, each against a written bar. A change to prompts or ranking shows a before and after |
| Tests never call a real model          | The xUnit suite uses a scripted fake chat client and a hashing embedder, so it runs offline and gives the same result every time                                   |

## 4. Agents and MCP: least privilege

When an AI client acts in the CRM through MCP, treat it like a new employee on day one.

- **It acts as one person.** A token belongs to a user, and every tool runs with that user's
  access. There's no service account that sees everything.
- **Tokens open one door.** `/mcp` accepts only personal tokens, and the rest of the API
  accepts only the browser cookie. Tokens are shown once, expire in 90 days and revoke on the
  next call.
- **Read by default.** Six of the seven tools only read. The one write tool,
  `create_draft_note`, adds a note labelled as written through MCP. It can't book, assign,
  cancel or contact anyone.
- **Assume the client's model is in the cloud.** Even when ours is local, an MCP client may not
  be. Contact details leave the server masked.
- **New write tools need two reviewers** and a written reason in the PR for why an agent needs
  it.

[docs/MCP.md](docs/MCP.md) has the details.

## 5. When the model is wrong

It will be. A 7B model running locally gets dates wrong, over-reads a passage, and returns an
empty list when a prompt is vague. The answer is design, not hope:

- Do arithmetic, dates, matching and ranking in code. Let the model read and write sentences.
- Use temperature 0 for anything we evaluate, so a change in the score means a change in the
  code.
- Name what you expect back. "One entry for each of these ids: 14, 22, 31" works where "an
  entry per technician" returned nothing.
- When an answer is wrong in a way that matters, add it to the eval (`evals/*.json`) so it
  stays fixed.
- Tell users plainly that answers can be wrong. The assistant's input box says so, and every
  drafted part of a call says which model drafted it.

## 6. Changing this document

Anyone can propose a change in a PR. Changes to sections 2 and 4 need the engineering lead's
review.
