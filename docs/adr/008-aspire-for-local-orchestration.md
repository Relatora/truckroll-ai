# ADR-008: Use Aspire to run the stack locally

- **Status:** Accepted
- **Date:** 2026-11-25

## Context

Truckroll is four moving parts on a developer's machine: MySQL, Redis, the ASP.NET Core API and
the Next.js app, plus Ollama on the host. A new engineer (or a reviewer with ten minutes) needs
all of them running, connected, and observable. The usual answer is a `docker-compose.yml`, a
`.env` file of connection strings, and a README section on starting things in the right order.

We also want traces from day one. Model calls are slow and occasionally wrong, and the first
question when an answer is bad is "what did retrieval return, and how long did the model take?"

## Decision

Use Aspire 17. `src/Truckroll.AppHost/AppHost.cs` declares every resource and its dependencies in
C#. One command (`aspire run`, or `dotnet run` on the AppHost) starts them in order, injects
connection strings, and opens a dashboard with logs, traces and metrics for all of them. Services
pick up shared defaults (OpenTelemetry, health checks, resilient HTTP) from
`Truckroll.ServiceDefaults`.

## Consequences

- Onboarding is "install Docker, the .NET SDK and Ollama, then run one command".
- Telemetry is OpenTelemetry from the start, so v0.2 only changes where it's exported (Datadog),
  not how it's produced.
- Aspire releases quickly and breaks things: 17.0 made `AddProject<T>` obsolete in favour of
  `AddDotnetProject`. Package versions are pinned centrally and upgraded on purpose.
- Aspire is a development-time tool here. Production deployment (v0.4) uses Kubernetes manifests,
  not Aspire's publishers, because that's what an operations team will want to own.

## Alternatives considered

- **Docker Compose.** Familiar, but it only starts containers. Connection strings, start order,
  and the dashboard would all be manual, and the API and web app would run outside it anyway.
- **Tye.** Archived.
