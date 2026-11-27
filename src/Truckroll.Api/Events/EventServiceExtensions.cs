using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Truckroll.Api.Features.Jobs;
using Truckroll.Api.Features.Notifications;

namespace Truckroll.Api.Events;

/// <summary>Registers the outbox, its relay, the subscribers, and the transport between them.</summary>
public static class EventServiceExtensions
{
    /// <summary>
    /// Job events go into the outbox in the same transaction as the change (docs/adr/005). The
    /// relay then hands them to Kafka, where each subscriber reads them as its own consumer group
    /// (docs/adr/009); or, with <c>Events:Transport = InProcess</c>, straight to the subscribers
    /// in this process, which is how the test suite runs without a broker.
    /// </summary>
    public static IHostApplicationBuilder AddTruckrollEvents(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var section = builder.Configuration.GetSection("Events");
        services.Configure<EventOptions>(section);
        var options = section.Get<EventOptions>() ?? new EventOptions();

        services.AddScoped<IJobEvents, OutboxJobEvents>();
        services.TryAddSingleton<OutboxSignal>();
        services.TryAddSingleton<OutboxCommitInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<EventMetrics>();
        services.AddSingleton<OutboxRelay>();
        services.AddHostedService(provider => provider.GetRequiredService<OutboxRelay>());

        // The subscribers. Each is a consumer group of its own under Kafka.
        services.AddSingleton<IJobChangeHandler, TechnicianAssignedDrafter>();

        if (options.Transport == EventTransportKind.Kafka)
        {
            // Aspire's producer brings the health check, metrics and tracing. Idempotent, every
            // replica acknowledging, and a bounded wait: a send to a broker that's down fails in
            // ten seconds, and the relay backs off and tries again, instead of hanging for five
            // minutes (the client default).
            builder.AddKafkaProducer<string, string>("kafka", settings =>
            {
                settings.Config.EnableIdempotence = true;
                settings.Config.Acks = Acks.All;
                settings.Config.MessageTimeoutMs = 10_000;
            });
            services.AddSingleton<IEventTransport, KafkaTransport>();
            services.AddHostedService<KafkaSubscriptions>();
        }
        else
        {
            services.AddSingleton<IEventTransport, InProcessTransport>();
        }

        return builder;
    }
}
