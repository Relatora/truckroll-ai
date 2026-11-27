# ADR-009: Send job events on one Kafka topic keyed by job, at least once, to idempotent consumers

- **Status:** Accepted
- **Date:** 2026-11-27

## Context

ADR-005 put every job change into a transactional outbox (built in #45) and planned Kafka
topics per event type: `job.created`, `job.assigned`, `job.completed`. Wiring it up raised three
questions that one sketch line didn't answer.

1. **Order.** Kafka keeps messages in order only within one partition of one topic. A job is
   often assigned, unassigned and assigned again within a minute. With a topic per type, the
   "unassigned" could be read before the "assigned" it undoes, and the board would show the job
   in the wrong lane.
2. **Delivery.** The outbox relay marks a row sent only after Kafka accepts it. If the API stops
   between those two steps, the event is sent again on restart. Consumers commit their place only
   after they've handled an event, so a consumer that stops mid-event reads it again. Exactly once,
   end to end, isn't on offer across a database and a broker without distributed transactions.
3. **Who reads what.** The board, the search index and the new notifications drafter all want
   every event, at their own pace, and the board needs only what happens while a browser is open.

## Decision

- **One topic, `truckroll.jobs`, keyed by job id**, with three partitions. Every event for a job
  lands in the same partition, so one job's events are always read in the order they happened.
  The event type travels in the payload and in an `event-type` header, so a consumer can skip
  what it doesn't need without parsing the body.
- **At least once, on purpose.** The producer is idempotent with `acks=all`, which stops the
  client itself from duplicating a message on a retry. The remaining duplicate (a crash between
  the send and the mark) is accepted. **Every consumer must be idempotent**: each event carries
  an `EventId` (a GUID stamped when it's written to the outbox), and a consumer that must not act
  twice keys on it. The notifications table has a unique index on it.
- **One consumer group per subscriber.** Each `IJobChangeHandler` names its subscription, and
  that name is its group: `truckroll.knowledge`, `truckroll.notifications`. Each keeps its own
  place in the stream, catches up after downtime, and can't hold the others back.
- **The board is live-only.** It gets a group per API process, starting from the latest message:
  each process pushes to its own browser connections, so every process must hear every event,
  and nobody needs yesterday's board updates replayed.
- **A failing handler is retried, not skipped.** The consumer seeks back and tries again after a
  pause. The board is the exception; a stale update is replaced by the next one.
- **The outbox, not the broker, is the durable record.** Locally, Kafka runs with no volume and
  starts empty each run. Anything not yet in Kafka is still in MySQL.
- **`Events:Transport` picks the transport.** `Kafka` under Aspire; `InProcess` for the test
  suite, which runs the same outbox and relay and hands events straight to the handlers.
  Kafka-specific behaviour has its own tests against a real broker.

## Consequences

- Order per job is guaranteed; order *across* jobs isn't, and nothing needs it.
- A new subscriber is one class: implement `IJobChangeHandler`, register it, and it gets a
  consumer group. It must state how it handles a second delivery, and review checks that.
- Three partitions cap a subscriber at three parallel consumers. Partitions can be added later,
  but that moves keys between partitions, so it's a planned change, not a casual one.
- Running two API instances needs one more step: only one relay may send. That's a lease, or
  `FOR UPDATE SKIP LOCKED` on the outbox, and it comes with hosting (v0.4).

## Alternatives considered

- **A topic per event type** (ADR-005's sketch): it reads well in a diagram, but loses per-job
  order, which is the property the board depends on.
- **Kafka transactions for exactly once:** they cover Kafka-to-Kafka work, not a MySQL write
  plus a publish. The outbox with idempotent consumers gets the same effect with less machinery.
- **Change data capture (Debezium) on the outbox table:** a solid choice at scale, but another
  service to run on every developer's laptop, for a relay of about a hundred lines.
