# ADR-001: Target .NET 10 LTS, not .NET 11

- **Status:** Accepted
- **Date:** 2026-11-25

## Context

.NET 11 shipped on 2026-11-10, two weeks before this repo started. It's a Standard Term Support
release: 24 months of patches, ending November 2028. .NET 10, released a year earlier, is the
Long Term Support release, patched until November 2028 as well, and it's the version the wider
ecosystem has caught up with.

A CRM is the system a home-services company runs on every day. The people who own it want fewer
upgrades, not newer syntax. And the libraries this repo needs move at their own pace: the MySQL
provider for EF Core (ADR-002) only shipped its EF Core 10 build in October 2026, and EF Core 11
needs .NET 11.

## Decision

Target `net10.0` everywhere, set once in `Directory.Build.props`. Pin the SDK in `global.json`
(10.0.404, rolling forward to later feature bands) so every machine builds with the same compiler.

## Consequences

- Every package this repo depends on has a stable .NET 10 build today. Nothing runs on a preview.
- The move to .NET 12 (the next LTS, due November 2027) is one property and a package update,
  planned for when .NET 10 has about a year of support left.
- We skip .NET 11's new features until then. Nothing in v0.1 needs them.

## Alternatives considered

- **.NET 11.** Newer, but STS, and the MySQL provider's EF Core 11 build is days old. Moving the
  foundation of a business system onto the newest release is the wrong default.
- **.NET 8.** Its support ended on 2026-11-10.
