# Observability

Everything the API knows about itself goes out as OpenTelemetry: traces, metrics and logs. Locally
it lands in the Aspire dashboard. Turn Datadog on and the same telemetry also goes to the
Datadog Agent ([ADR-010](adr/010-opentelemetry-to-datadog-agent.md)).

## What's measured

| Signal | Where it comes from | What it tells you |
| ------ | ------------------- | ----------------- |
| `http.server.request.duration` | ASP.NET Core | Requests, latency and status code, by route |
| A span per model call | Microsoft.Extensions.AI | Provider (`gen_ai.provider.name`), model (`gen_ai.request.model`), tokens in and out (`gen_ai.usage.*`) and how long it took. Prompts and answers are left out |
| `gen_ai.client.operation.duration` | Microsoft.Extensions.AI | Model call time, by model and provider. `error.type` is set when a call fails |
| `gen_ai.client.token.usage` | Microsoft.Extensions.AI | Tokens, split by `gen_ai.token.type` (input, output) |
| `truckroll.events.published` | The outbox relay | Job events handed to the transport, by `event.type` (job.booked, job.assigned, ...) |
| `truckroll.outbox.delay` | The outbox relay | Seconds from the change committing to its event leaving the outbox |
| `truckroll.outbox.send_failures` | The outbox relay | Times Kafka refused an event. It only appears after the first failure |
| Spans for MySQL, Redis, Kafka, HttpClient | Their client libraries | Where a slow request spent its time |

Embeddings are model calls too. The provider is `ollama` and the model `nomic-embed-text`. A
knowledge reindex produces a burst of them.

## Turning Datadog on locally

You need a Datadog account (the free trial covers this). Create an API key under
**Organization Settings → API Keys**, then:

```powershell
dotnet user-secrets --project src/Truckroll.AppHost set "Parameters:datadog-api-key" "<your key>"
# Only if your account isn't on US1 (datadoghq.com):
dotnet user-secrets --project src/Truckroll.AppHost set "Parameters:datadog-site" "datadoghq.eu"
```

Start the stack as usual. A `datadog-agent` resource joins it, pinned to
`gcr.io/datadoghq/agent:7.85.2` and listening for OTLP over gRPC. The API sends it a copy of
everything, and the Aspire dashboard keeps working. Expect the Agent to use a few hundred MB of
memory.

To turn it off, remove the secret:

```powershell
dotnet user-secrets --project src/Truckroll.AppHost remove "Parameters:datadog-api-key"
```

Without that key the Agent doesn't run, and nothing leaves the machine.

## The dashboard

`ops/datadog/truckroll-dashboard.json` is a Datadog dashboard in three groups:

- **API:** requests per second and p95 latency by route, and 5xx errors as a share of requests.
- **Models:** p95 call time by model and provider, tokens in and out, and failed calls.
- **Job events:** events by type, outbox delay (p50 and p95), and send failures.

To install it, go to **Dashboards → New Dashboard → ⚙ → Import dashboard JSON** and paste the
file. Or use the API:

```powershell
curl.exe -X POST "https://api.datadoghq.com/api/v1/dashboard" `
  -H "DD-API-KEY: $env:DD_API_KEY" -H "DD-APPLICATION-KEY: $env:DD_APP_KEY" `
  -H "Content-Type: application/json" --data-binary "@ops/datadog/truckroll-dashboard.json"
```

The `service` and `env` template variables scope every chart. Locally the service is `api` and
the environment `local`.

`DashboardContractTests` reads the file in the quality gate, so a renamed metric fails a test
instead of leaving a chart empty.

## Checked so far, and not yet

- **Checked on the real stack:** the second exporter was pointed at a local OpenTelemetry
  Collector. Logs, traces and metrics all arrived from `service.name=api`, with
  `gen_ai.provider.name=ollama`, the model names, token counts split by type, and
  `truckroll.events.published` tagged `event.type=job.noted`. The Aspire dashboard got its copy
  at the same time.
- **Not yet checked against a live Datadog account.** The dashboard queries follow Datadog's
  documented OTLP mapping: histograms arrive as distributions, so `count:` gives a rate and
  `p95:` a percentile. The first import into a trial is where a wrong guess would show up.
- **Not built yet:** time to dispatch (from booking to assignment) and failed index updates.
  Both appear in the roadmap's dashboard list.
