# ADR-011: Sync offline changes as idempotent commands; the office wins on assignment

- **Status:** Accepted
- **Date:** 2026-11-28

## Context

Technicians work in basements and crawl spaces with no signal. The technician app (#22) has to
let them start a job, write notes and close it with no connection, and send all of that when
the signal comes back. That can be minutes or hours later.

By then the office may have moved the job. A dispatcher reassigns it to someone closer, or a
CSR cancels it because the customer called back. The phone can't know until it syncs, so the
server needs rules for whose change wins. The technician also needs to be told what happened in
words they can act on.

Phones on a bad connection send things twice. A sync can reach the server and lose its answer
on the way back, and the phone has no way to tell that from a sync that never arrived.

## Decision

**Send commands, not state.** The phone queues what the technician did: `start`, `complete`
(with the resolution) or `note`, each with:

- an id the phone generates (a UUID)
- the time it was made, by the phone's clock

It never sends "the job now looks like this". The server applies each command in order, in its
own transaction, and publishes the usual job event through the outbox, so the dispatcher's
board moves live.

**Idempotent by change id.** The server records every change id with its outcome
(`FieldChanges`). The same id arriving again gets the recorded answer and changes nothing. A
change id that belongs to a different account is refused.

**Who wins:**

| Change | Wins | Why |
|---|---|---|
| A note | The technician, always | A note conflicts with nothing |
| Start or complete, on a job reassigned or unassigned since | The office | Assignment is a dispatch decision. Two technicians must never both think it's theirs |
| Start or complete, on a cancelled job | The office | The customer told the office |
| Complete, on a job someone already completed | First one in | The job is done. The second resolution becomes a note |

**Nothing typed is lost.** When the office wins, the technician's resolution is saved as a note
on the job, saying it was written in the field and not applied. The sync answer explains it in
a sentence: "J-24051 was given to Tyler Brooks while you were offline. Your resolution was saved
as a note on the job."

**When it happened.** A completion records the phone's time, not the time the signal came back.
A time in the future (a wrong clock) is clamped to now.

**Access.** The field endpoints take a personal token (the `trk_` scheme MCP uses), and only for
the Technician role. Signing in on the phone issues one, named for the device, and it can be
revoked on the token page like any other. A cookie can't use the field API, and a field token
can't open the web app.

Two read rules are new in `AccessScope`:

- **`ServiceHistory`:** past jobs at homes the technician may see, whoever did them. The last
  visit's resolution is what a technician needs at the door.
- **`JobsToExplain`:** jobs in the technician's own branch. Sync uses it only to explain a job
  that's no longer theirs, and reads its status and current technician, never the customer.

## Consequences

- **Retrying is always safe,** so the phone's sync loop stays simple: send the queue, drop what
  came back answered, keep the rest.
- **The server holds the rules.** The phone never needs to know them; it shows the explanation
  it's given. A rule change ships without an app update.
- **One technician on one job at a time.** If two phones signed in as the same technician
  complete the same job, the first completion wins and the second becomes a note. That's
  acceptable for one technician per job; a crew would need merging rules.
- **`FieldChanges` grows by every tap.** It's small (a row per change) and useful as an audit
  trail. A retention job can trim rows older than the longest time a phone could stay offline.
- **A field token is a personal token, so it also works on `/mcp`,** as the same technician with
  the same scope. It grants nothing the person couldn't already do. Separate token purposes can
  come if a phone ever needs less than its owner.
- **A technician can learn a branch job's status and assignee** by guessing its id. The
  customer's details stay hidden. We accept that, since branch staff see the board anyway.

## Alternatives considered

- **State sync with last-write-wins** (send the whole job, newest timestamp wins): it depends on
  phone clocks and silently discards the loser's change. That's the failure this design exists
  to prevent.
- **Version numbers and a 409 on conflict:** honest, but it pushes the merge onto the phone and
  the technician ("resolve this conflict") at the worst moment. Fixed rules with an explanation
  do the merge once, on the server.
- **CRDTs or a sync framework** (PowerSync, Realm): built for collaborative editing of shared
  data. Three commands and four rules don't need one, and a framework would be a second source
  of truth beside MySQL.
- **A cookie for the phone:** React Native's cookie handling differs by platform, and a token
  can be named, listed and revoked per device.
