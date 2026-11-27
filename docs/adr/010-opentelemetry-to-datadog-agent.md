# ADR-010: Send OpenTelemetry to the Datadog Agent, not the Datadog tracer

- **Status:** Accepted
- **Date:** 2026-11-27

## Context

Operations works in Datadog, so the API's traces, metrics and logs need to end up there, with
a dashboard for API latency, model latency and token use, and job events.

The API already produces all of it through OpenTelemetry. ASP.NET Core and HttpClient
instrumentation cover the requests. Microsoft.Extensions.AI's `UseOpenTelemetry()` writes a span
per model call, with the provider, the model and token counts, plus `gen_ai.client.*` metrics.
Locally, everything goes over OTLP to the Aspire dashboard.

Datadog offers two ways in:

- **The Datadog .NET tracer** (`dd-trace-dotnet`) instruments the process itself.
- **The Datadog Agent** has accepted OTLP since 7.32 (traces and metrics) and 7.48 (logs).

## Decision

Keep the code on OpenTelemetry and point OTLP at the Datadog Agent.

- **In a cluster (v0.4):** the Agent runs on each node, and `OTEL_EXPORTER_OTLP_ENDPOINT` names
  it. That is the only exporter.
- **Locally:** the Aspire dashboard stays the default destination. When a Datadog API key is
  set, the AppHost starts the Agent and the API sends a second copy to it
  (`Telemetry:Datadog:OtlpEndpoint`). With no key, nothing runs and nothing leaves the machine.
- **Our own numbers** go on one meter, `Truckroll`: events published by type, outbox delay, and
  send failures.
- **The dashboard** is committed as JSON (`ops/datadog/`). A test fails if it reads a metric the
  API doesn't emit, or one from a meter that isn't exported.

## Consequences

- **Vendor-neutral code.** Changing vendors, or adding an OpenTelemetry Collector in front of
  everything, means changing an endpoint, not the code. The same telemetry shows in the
  Aspire dashboard and in Datadog, so what a developer sees locally is what operations sees.
- **Model telemetry keeps OpenTelemetry's GenAI names**, such as `gen_ai.provider.name` and
  `gen_ai.usage.input_tokens`. Those names are the industry convention, not Datadog's own.
- **Prompts and answers never reach Datadog.** The model pipelines run with sensitive data off,
  and the existing span test checks that a prompt never appears in a span.
- **We lose the tracer's extras:** automatic database spans from its own integrations,
  continuous profiling, and Datadog's own LLM Observability product. MySqlConnector and the
  Kafka client already emit OpenTelemetry of their own, so the trace gap is small. Profiling
  is a paid feature, outside the free trial this milestone is scoped to.
- **Two exporters locally means the API sends everything twice.** That's fine on a laptop, and
  a cluster only ever has one exporter.
- **Reversing this** means adding `dd-trace-dotnet` to the container image and dropping the
  second exporter. Spans would get Datadog's names, so the dashboard would need its queries
  rewritten.

## Alternatives considered

- **The Datadog .NET tracer.** The richest Datadog experience, but it ties the code and the
  container image to one vendor, and the Aspire dashboard would need a second telemetry path.
- **An OpenTelemetry Collector with the Datadog exporter.** It's the same idea with one more hop,
  and the Collector is one more thing to run and configure. Worth it once there are several
  backends or sampling rules. One Agent is enough for now.
- **Point the API at the Agent only, locally too.** That's simpler, but the Aspire dashboard
  would go dark the moment someone turned Datadog on.
