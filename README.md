# Truckroll

A field-service CRM for heating, cooling and plumbing companies, with AI where the day actually
goes: finding what's installed at a house, picking the right technician, and turning a phone call
into a ticket.

Built on ASP.NET Core 10, EF Core, MySQL, Redis, SignalR and Next.js. Work in progress toward
[v0.1.0](https://github.com/Relatora/truckroll-ai/milestone/1); the plan is in
[docs/DESIGN.md](docs/DESIGN.md).

## Running it locally

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), Docker, Node 22+
and [Ollama](https://ollama.com).

```bash
ollama pull qwen2.5:7b
ollama pull nomic-embed-text
cd web && npm install && cd ..
dotnet run --project src/Truckroll.AppHost --launch-profile http
```

The console prints a link to the Aspire dashboard, which shows every resource, its logs and its
traces. The web app is at http://localhost:3000. If you have the Aspire CLI
(`dotnet tool install -g Aspire.Cli`), `aspire run` does the same thing.

### Models

Chat runs on `qwen2.5:7b` through Ollama, and embeddings on `nomic-embed-text`. To have Claude
answer instead, change one setting and add a key. Embeddings stay local either way (see
[ADR-004](docs/adr/004-models-behind-microsoft-extensions-ai.md)).

```bash
dotnet user-secrets --project src/Truckroll.Api set "AI:Provider" Claude
dotnet user-secrets --project src/Truckroll.Api set "AI:Claude:ApiKey" <your key>
```

`GET /api/dev/ai-check` (development only) asks the chat model one question, embeds one string,
and reports which models answered and how long each took. Each call shows up as a span in the
Aspire dashboard's traces.

## Quality gate

```powershell
./scripts/ci.ps1     # or ./scripts/ci.sh
```

`dotnet format`, a Release build with warnings as errors, xUnit tests against real MySQL and
Redis in Testcontainers, ESLint, `tsc` and a production `next build`. GitHub Actions is locked on
this account, so the gate runs locally before every merge; the workflow in `.github/workflows`
runs the same script once it's unlocked.

## License

Copyright (c) 2026 Amr Abdallah. All rights reserved.

The source is public to read, not to use. You may view it to evaluate my work, but you may not run, copy, modify, redistribute or train AI models on it. See [LICENSE](LICENSE) for the full terms.
