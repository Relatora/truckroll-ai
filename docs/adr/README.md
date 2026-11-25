# Architecture decision records

Short write-ups of decisions that shape the codebase: what we chose, why, and what it costs us.
If you're about to ask "why is it done this way?", the answer is probably here. If it isn't,
that's a sign we owe an ADR.

| #                                             | Decision                                   | Status   |
| --------------------------------------------- | ------------------------------------------ | -------- |
| [001](001-dotnet-10-lts.md)                   | Target .NET 10 LTS, not .NET 11            | Accepted |
| [002](002-mysql-provider-for-ef-core-10.md)    | Reach MySQL through the Microting EF Core provider | Accepted |
| [008](008-aspire-for-local-orchestration.md)  | Use Aspire to run the stack locally        | Accepted |

Numbers 003 to 007 are reserved for decisions already planned in [DESIGN.md](../DESIGN.md)
(retrieval in Redis, model abstraction, Kafka timing, same-origin API, dispatch
ranking). They're written when the code that implements them lands.

New ADRs start from [the template](000-template.md) and add a row here. Never edit an accepted
ADR's decision. Write a new one that supersedes it, so the history stays readable.
