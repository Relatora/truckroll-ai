using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Truckroll.Api.Events;
using Truckroll.Api.Tests.Infrastructure;

namespace Truckroll.Api.Tests.Observability;

/// <summary>
/// Keeps the Datadog dashboard honest. A renamed metric or a meter nobody exports doesn't fail a
/// build; it turns a chart quietly empty, and the first person to notice is on call. These tests
/// fail first.
/// </summary>
public sealed partial class DashboardContractTests(TruckrollApiFactory factory)
{
    private static readonly string DashboardPath = Path.Combine(AppContext.BaseDirectory, "ops", "datadog", "truckroll-dashboard.json");

    [Fact]
    public async Task Every_metric_the_dashboard_reads_is_emitted_and_exported()
    {
        // Instrument name -> meter name, for every instrument in this process. A listener is told
        // about existing instruments as soon as it starts, and about new ones as they're created.
        var instruments = new ConcurrentDictionary<string, string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) => instruments.TryAdd(instrument.Name, instrument.Meter.Name),
        };
        listener.Start();

        // Make sure each family exists: an HTTP request (ASP.NET Core), a model call (the
        // gen_ai.* histograms are created with the pipeline) and the event metrics.
        using var client = factory.CreateClient();
        await client.GetAsync("/api/status", TestContext.Current.CancellationToken);
        await factory.Services.GetRequiredService<IChatClient>().GetResponseAsync("dashboard-contract-0c4e", cancellationToken: TestContext.Current.CancellationToken);
        factory.Services.GetRequiredService<EventMetrics>();

        var queried = DashboardMetrics();
        Assert.NotEmpty(queried);
        Assert.All(queried, metric =>
        {
            Assert.True(instruments.TryGetValue(metric, out var meter), $"The dashboard reads {metric}, but nothing in the API emits it.");
            Assert.True(ServiceDefaultsExtensions.ExportedMeters.Contains(meter), $"{metric} comes from meter {meter}, which service defaults don't export.");
        });
    }

    [Fact]
    public async Task Published_events_are_counted_by_type()
    {
        var types = new ConcurrentBag<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Name == "truckroll.events.published")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "event.type" && tag.Value is string type)
                {
                    types.Add(type);
                }
            }
        });
        listener.Start();

        factory.Services.GetRequiredService<EventMetrics>().Published("job.assigned", TimeSpan.FromMilliseconds(40));
        await Task.CompletedTask;

        Assert.Contains("job.assigned", types);
    }

    [Fact]
    public void Telemetry_starts_with_both_the_dashboard_and_Datadog_as_destinations()
    {
        // UseOtlpExporter() refuses to sit beside a named exporter, at startup. This is the shape
        // the API runs in when the AppHost adds the Datadog Agent.
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317",
            [ServiceDefaultsExtensions.DatadogEndpointKey] = "http://localhost:14317",
        });
        builder.ConfigureOpenTelemetry();

        using var host = builder.Build();
        Assert.NotNull(host.Services.GetService<TracerProvider>());
        Assert.NotNull(host.Services.GetService<MeterProvider>());
        Assert.NotNull(host.Services.GetService<LoggerProvider>());
    }

    [Fact]
    public void The_dashboard_is_scoped_by_service_and_environment()
    {
        // Every query filters on the template variables, so one dashboard serves local, staging
        // and production without editing it.
        Assert.All(DashboardQueries(), query => Assert.Contains("service:$service.value", query, StringComparison.Ordinal));
    }

    private static List<string> DashboardMetrics() =>
        [.. DashboardQueries().Select(q => MetricName().Match(q)).Where(m => m.Success).Select(m => m.Groups["name"].Value).Distinct()];

    private static List<string> DashboardQueries()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(DashboardPath));
        var queries = new List<string>();
        Collect(document.RootElement, queries);
        return queries;

        static void Collect(JsonElement element, List<string> into)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name == "query" && property.Value.ValueKind == JsonValueKind.String)
                        {
                            into.Add(property.Value.GetString()!);
                        }
                        else
                        {
                            Collect(property.Value, into);
                        }
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Collect(item, into);
                    }

                    break;
            }
        }
    }

    // "p95:http.server.request.duration{...}": the aggregator, then the metric up to the filter.
    [GeneratedRegex(@"^[a-z0-9]+:(?<name>[a-z0-9_.]+)\{")]
    private static partial Regex MetricName();
}
