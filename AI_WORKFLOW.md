# AI workflow

How a team should use AI on a codebase like this one: the coding assistants we work with, and
the AI features we ship to the office and the field. It's short on purpose. A policy nobody
remembers is no policy at all.

The one-line version: **AI makes us faster. It doesn't change who is accountable.** The person
who opens the PR owns every line in it, however it was written.

## 1. Coding with an assistant

Use one. Most of us are faster with an assistant for scaffolding, tests, refactors, reading
unfamiliar code and first drafts of docs. The rules keep the quality bar where it is.

**You review it before anyone else does.** If you couldn't explain a line in review, it
doesn't go in the PR. "The assistant wrote it" is not an answer to a review question.

**Tests are not optional, and they're not the assistant's to grade.** Generated code comes
with a test that fails without the change. Read generated tests closely: a common failure is a
test that asserts whatever the code happens to do, rather than what it should do.

**Watch for the usual failure modes:**

- APIs that don't exist, or exist in another version. .NET and Next.js both move fast; check
  against the package you actually restored, not the assistant's memory. For example, Aspire 17
  made `AddProject<T>` obsolete, and assistants trained before that still suggest it.
- Authorization quietly dropped. A query that reads `db.Customers` without going through the
  Access layer is the most expensive bug in this codebase. Reviewers check it on every PR.
- Confident comments that describe what the code should do rather than what it does.
- Plausible but wrong trade details, like a made-up licence class or a warranty term nobody
  asked for.

**Keep PRs small.** It's easy to generate a 2,000-line PR. Nobody can review one. The size
guidance in [CONTRIBUTING.md](CONTRIBUTING.md) applies whatever wrote the code.

## 2. What never goes into a prompt

This applies to any assistant or model that isn't running on our own hardware.

- **Customer data:** names with contact details, phone numbers, emails, addresses, door and
  alarm codes, payment details, call recordings. Use the seeded demo data, or mask it first.
- **Secrets:** `.env` files, user secrets, API keys, tokens, database dumps, private keys.
- **Anything under NDA**, a supplier agreement or a customer contract.

If you're not sure, it doesn't go in. Ask first.

## 3. AI features in the product

The same principles, built into the code so nobody has to remember them.

| Principle                              | How it's enforced                                                                                         |
| -------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| The model sees only what the user may  | Retrieval and every MCP tool filter through the Access layer, inside the query                            |
| Personal data stays out of prompts     | The PII masker runs before text is embedded or sent to a model, and again on MCP output                   |
| Answers show their work                | Every answer cites numbered passages. "I don't know" is an acceptable answer                              |
| Model output is data, not code         | Rendered as text, never as HTML. Never executed, never sent to a customer by itself                       |
| A person presses approve               | Call summaries, proposed jobs and follow-ups are drafts. Nothing is created or sent without a click       |
| Code decides, the model explains       | Dispatch ranking is deterministic C#. The model writes the reason and can't change the order              |
| Quality is measured                    | `tools/Truckroll.Eval` scores retrieval, dispatch and extraction. Changes show a before and after         |
| Tests never call a real model          | A fake chat client and a deterministic fake embedder. The suite runs offline, same result every time      |
| Usage is bounded                       | Per-user rate limits on the assistant and on MCP, plus server-side timeouts on every model call           |

## 4. Agents and MCP: least privilege

When an AI client acts in the CRM through MCP, treat it like a new employee on day one.

- **It acts as one person.** A token belongs to a user, and every tool runs with that user's
  role and branch. There's no service account that sees everything.
- **Tokens open one door.** A personal access token only works on `/mcp`, not on the rest of
  the API. It's shown once, stored hashed, and revocable in one click.
- **Read by default.** Every tool reads, except one that writes a draft note. Drafts are
  labelled as coming from MCP and never reach a customer by themselves.
- **Assume the client's model is in the cloud.** Even when our own model is local, an MCP
  client may not be. Contact details leave the server masked.
- **New write tools need two reviewers** and a written reason in the PR for why the agent
  needs it.

## 5. When the model is wrong

It will be. A 7B model running locally gets arithmetic wrong and sometimes over-reads a
passage. The answer is design, not hope:

- Do arithmetic, filtering and ranking in code or SQL, and let the model choose words.
- Validate structured output against the schema, retry once, then fail visibly rather than
  guess.
- Keep prompts short and specific. A worked example in a prompt can make a small model parrot
  the example.
- When an answer is wrong in a way that matters, add it to the eval set so it stays fixed.
- Tell people plainly that answers can be wrong. The assistant's input box says so.

## 6. Changing this document

Anyone can propose a change in a PR. Changes to sections 2 and 4 need the technology lead's
review.
