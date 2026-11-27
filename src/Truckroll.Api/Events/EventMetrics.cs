using System.Diagnostics.Metrics;

namespace Truckroll.Api.Events;

/// <summary>
/// The numbers operations watches for the event pipeline: how many job events went out, of which
/// kind, how long each waited in the outbox, and how often the transport refused one.
/// </summary>
/// <remarks>
/// Written to the <c>Truckroll</c> meter, which service defaults export over OTLP alongside the
/// ASP.NET Core and model metrics. The Datadog dashboard (ops/datadog) reads these by name, and a
/// test checks that every name it reads is one this code emits.
/// </remarks>
public sealed class EventMetrics
{
    /// <summary>The meter name. Matches <c>ServiceDefaultsExtensions.ActivitySourceName</c>.</summary>
    public const string MeterName = "Truckroll";

    private readonly Counter<long> _published;
    private readonly Histogram<double> _delay;
    private readonly Counter<long> _sendFailures;

    public EventMetrics(IMeterFactory meters)
    {
        var meter = meters.Create(MeterName);
        _published = meter.CreateCounter<long>(
            "truckroll.events.published", unit: "{event}",
            description: "Job events handed to the transport, by type (job.assigned, job.booked, ...).");
        _delay = meter.CreateHistogram(
            "truckroll.outbox.delay", unit: "s",
            description: "Time from the change committing to its event being handed on.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 300] });
        _sendFailures = meter.CreateCounter<long>(
            "truckroll.outbox.send_failures", unit: "{failure}",
            description: "Times the transport refused an event. The relay backs off and retries the same one.");
    }

    public void Published(string type, TimeSpan waited)
    {
        var tag = new KeyValuePair<string, object?>("event.type", type);
        _published.Add(1, tag);
        _delay.Record(Math.Max(0, waited.TotalSeconds), tag);
    }

    public void SendFailed(string type) =>
        _sendFailures.Add(1, new KeyValuePair<string, object?>("event.type", type));
}
