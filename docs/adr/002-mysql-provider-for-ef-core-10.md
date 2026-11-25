# ADR-002: Reach MySQL through the Microting EF Core provider

- **Status:** Accepted
- **Date:** 2026-11-25

## Context

The CRM's records live in MySQL 8.4 (the current LTS line), accessed through EF Core 10. For
years the obvious provider was Pomelo (`Pomelo.EntityFrameworkCore.MySql`). It has the best
LINQ translation for MySQL and most existing .NET + MySQL codebases use it.

Pomelo stopped at EF Core 9. Its 9.0.0 package pins EF Core Relational to 9.0.x, there's no
EF Core 10 build (not even a preview), and the tracking issue is still open a year after
EF Core 10 shipped. Any team on Pomelo hits this the day they upgrade to .NET 10.

Two maintained options exist:

| Provider | EF Core 10 | Notes |
| --- | --- | --- |
| `Microting.EntityFrameworkCore.MySql` 10.0.14 | Stable | A continuation of Pomelo by one company. Same API (`UseMySql`, `ServerVersion`), same LINQ translation, built on MySqlConnector. Tracks EF patch releases within days |
| `MySql.EntityFrameworkCore` (Oracle) 10.0.12 | Stable | Vendor-backed. Different API (`UseMySQL`, no `ServerVersion`), different SQL and migration output, historically weaker translation |

## Decision

Use Microting, pinned in `Directory.Packages.props`, with central transitive pinning so EF
Core can't arrive at two patch levels. EF shares the `MySqlDataSource` that Aspire registers, so
queries use one traced, health-checked connection pool.

## Consequences

- Code written against Pomelo moves over unchanged, which matters for any team migrating an
  existing CRM: the upgrade is a package swap, not a rewrite of queries and migrations.
- We depend on one company's maintenance. The mitigation is that the provider sits behind
  `UseMySql` in one file (`Truckroll.Api/Data/DataServiceExtensions.cs`) and the migrations are
  plain EF. If Microting stalls, switching to Oracle's provider is a contained change plus a
  regenerated migration, and the integration tests run against real MySQL to catch translation
  differences.
- Sharing a data source means the provider can't add its required connection options itself.
  `AllowUserVariables=True;UseAffectedRows=False` are set on the connection string explicitly;
  without them the provider refuses to start.

## Alternatives considered

- **Stay on EF Core 9 with Pomelo.** EF Core 9 went out of support on 2026-11-10.
- **Oracle's provider.** The safer name, but a different API, different migration output, and
  weaker LINQ translation. It's the fallback if Microting stalls.
- **Dapper over MySqlConnector.** Fine for read-heavy reporting queries later, but giving up
  migrations and change tracking for the whole CRM is the wrong trade.
