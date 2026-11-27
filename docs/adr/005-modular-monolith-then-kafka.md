# ADR-005: Build one API with in-process events now, and add an outbox and Kafka in v0.2

- **Status:** Accepted
- **Date:** 2026-11-27

## Context

The company this CRM is modelled on runs Kafka, and a field-service business is event-shaped:
a job is booked, assigned, started, finished. Other parts of the business want to hear about
those moments: the dispatch board, the search index, customer notifications, accounting.

v0.1 has one team (me), one deployable and two weeks. Every consumer of a job change today
lives in the same process as the code that saves it:

- the dispatch board's SignalR hub, which moves the job on every open board
- the knowledge queue, which re-indexes the job's notes for search
- the log

Kafka on day one would add a broker to run locally, a schema to version, and the hard part of
event-driven systems (an event published but the transaction rolled back, or the reverse)
before there's a second service to read the events.

## Decision

v0.1 is a modular monolith: one ASP.NET Core API, split into feature folders
(`Features/Jobs`, `Features/Dispatch`, `Features/Calls` and so on) that talk through small
interfaces, not through each other's tables.

Job changes are already events. Every endpoint that changes a job publishes a `JobChanged`
record through `IJobEvents` after the save. `InProcessJobEvents` fans each one out to every
registered `IJobChangeHandler`. A handler that fails is logged and never undoes the saved job.

v0.2 swaps the transport, not the shape:

1. The save and an `outbox` row are written in **one MySQL transaction**, so an event exists
   exactly when its change does.
2. A relay publishes outbox rows to Kafka topics (`job.created`, `job.assigned`,
   `job.completed`), keyed by job id so each job's events stay in order.
3. Consumers are idempotent, so a redelivered event doesn't double anything.
4. The in-process handlers become Kafka consumers one at a time, starting with
   notifications, which doesn't exist yet and so has nothing to migrate.

## Consequences

- **Easier now:** one process to run, debug and test. The hub and the index get changes the
  instant they're saved, and tests need no broker.
- **The seam is already there.** Endpoints publish through `IJobEvents` and never call the hub
  or the index directly. Moving to Kafka changes the registration in `Program.cs` and adds the
  outbox; it doesn't touch an endpoint.
- **What v0.1 can lose:** an in-process event is lost if the process dies between the save and
  the handler. For the board that means a stale view until the next refresh, and for the
  index, a job that's searchable a rebuild later. That's acceptable for a demo, and it's
  exactly what the outbox fixes.
- **What it can't do:** nothing outside the API can hear about a job. That's the first thing
  v0.2 adds.

## Alternatives considered

- **Kafka from the start.** It would prove the stack sooner, but it would add a broker to every
  developer's machine and dual-write bugs before any second consumer exists.
- **Microservices per feature.** They split one team's work across deployables, network calls
  and versioned contracts, all to solve an organisational problem this codebase doesn't have yet.
- **Publish to Kafka straight from the endpoint, with no outbox.** That's the classic
  dual-write: the save commits and the publish fails, or the reverse. The outbox costs one
  table and one relay, and removes the whole class of bug.
