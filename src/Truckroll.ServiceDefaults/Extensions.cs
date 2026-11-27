using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Defaults every Truckroll service starts with: OpenTelemetry (logs, traces, metrics),
/// health checks, service discovery and resilient outgoing HTTP.
/// </summary>
/// <remarks>
/// Telemetry goes out over OTLP when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set. Locally that's
/// the Aspire dashboard; in a cluster it's the Datadog Agent on the node (docs/adr/010). The code
/// doesn't change between the two, only the endpoint.
/// </remarks>
public static class ServiceDefaultsExtensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    /// <summary>
    /// The trace source every Truckroll component writes its own spans to (model calls,
    /// retrieval, dispatch scoring), on top of the ASP.NET Core and HTTP instrumentation.
    /// </summary>
    public const string ActivitySourceName = "Truckroll";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// The trace sources and meters exported, besides the ASP.NET Core and HTTP instrumentation.
    /// </summary>
    /// <remarks>
    /// <c>Experimental.Microsoft.Extensions.AI</c> is where <c>UseOpenTelemetry()</c> on the model
    /// pipelines writes: a span per call (model, provider, token counts) and the
    /// <c>gen_ai.client.*</c> metrics. A meter that isn't listed here is measured and thrown away,
    /// which is why a test checks the Datadog dashboard against this list.
    /// </remarks>
    public static readonly IReadOnlyList<string> ExportedMeters =
    [
        ActivitySourceName,
        GenAiSourceName,
        "Microsoft.AspNetCore.Hosting",
        "Microsoft.AspNetCore.Server.Kestrel",
        "System.Net.Http",
    ];

    /// <summary>The source and meter Microsoft.Extensions.AI writes model telemetry to.</summary>
    public const string GenAiSourceName = "Experimental.Microsoft.Extensions.AI";

    /// <summary>
    /// Where to send a second copy of everything, for Datadog: the Agent's OTLP gRPC receiver.
    /// Unset by default. The AppHost sets it when a Datadog API key is configured.
    /// </summary>
    public const string DatadogEndpointKey = "Telemetry:Datadog:OtlpEndpoint";

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // The default destination is OTEL_EXPORTER_OTLP_ENDPOINT: the Aspire dashboard locally, a
        // node's Datadog Agent in a cluster. Locally, a second one can be added for Datadog, so
        // the dashboard keeps working while the same telemetry also goes to the Agent.
        var primary = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        var datadog = builder.Configuration[DatadogEndpointKey] is { Length: > 0 } endpoint ? new Uri(endpoint) : null;

        builder.Services.AddOpenTelemetry()
            .WithLogging(
                logging => AddExporters(logging, primary, datadog, (l, n, c) => l.AddOtlpExporter(n, c)),
                options =>
                {
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter([.. ExportedMeters]);
                AddExporters(metrics, primary, datadog, (m, n, c) => m.AddOtlpExporter(n, c));
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(builder.Environment.ApplicationName, ActivitySourceName, GenAiSourceName)
                    .AddAspNetCoreInstrumentation(options =>
                        // Health probes run every few seconds and would drown the real traffic.
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                    .AddHttpClientInstrumentation();
                AddExporters(tracing, primary, datadog, (t, n, c) => t.AddOtlpExporter(n, c));
            });

        return builder;
    }

    /// <summary>
    /// Adds the OTLP exporters to one signal. Per signal rather than <c>UseOtlpExporter()</c>,
    /// because that one can't be combined with a second, named exporter.
    /// </summary>
    private static void AddExporters<T>(T signal, bool primary, Uri? datadog, Action<T, string?, Action<OtlpExporterOptions>?> add)
    {
        if (primary)
        {
            add(signal, null, null); // Endpoint, protocol and headers come from the OTEL_* variables.
        }

        if (datadog is not null)
        {
            add(signal, "datadog", options =>
            {
                options.Endpoint = datadog;
                options.Protocol = OtlpExportProtocol.Grpc;
                // Named options still read OTEL_EXPORTER_OTLP_HEADERS, which holds the Aspire
                // dashboard's key. The Agent doesn't need it, so it doesn't get it.
                options.Headers = string.Empty;
            });
        }
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Maps <c>/health</c> (every check, used for readiness) and <c>/alive</c> (liveness only).
    /// Development only: a public health endpoint tells an attacker which dependencies you run.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthEndpointPath);
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("live"),
            });
        }

        return app;
    }
}
