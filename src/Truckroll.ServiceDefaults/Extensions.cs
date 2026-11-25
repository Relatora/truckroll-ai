using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Defaults every Truckroll service starts with: OpenTelemetry (logs, traces, metrics),
/// health checks, service discovery and resilient outgoing HTTP.
/// </summary>
/// <remarks>
/// Telemetry goes out over OTLP when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set. Locally that's
/// the Aspire dashboard; in production it's whatever collector sits in front of Datadog
/// (milestone v0.2.0). The code doesn't change between the two, only the endpoint.
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

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(ActivitySourceName))
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName, ActivitySourceName)
                // Microsoft.Extensions.AI emits GenAI spans (model, tokens, latency) here.
                .AddSource("Experimental.Microsoft.Extensions.AI")
                .AddAspNetCoreInstrumentation(options =>
                    // Health probes run every few seconds and would drown the real traffic.
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                        && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
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
