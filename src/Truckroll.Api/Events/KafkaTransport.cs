using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Events;

/// <summary>How job events travel, from the <c>Events</c> configuration section.</summary>
public sealed class EventOptions
{
    /// <summary><c>InProcess</c> (tests, and a machine without Kafka) or <c>Kafka</c> (the AppHost sets this).</summary>
    public EventTransportKind Transport { get; set; } = EventTransportKind.InProcess;

    /// <summary>
    /// One topic for every job event, keyed by job id. Kafka orders messages only within a
    /// partition, so one job's events must share one (docs/adr/009).
    /// </summary>
    public string Topic { get; set; } = "truckroll.jobs";

    /// <summary>Room for consumers to work in parallel, one job never split across two.</summary>
    public int Partitions { get; set; } = 3;
}

public enum EventTransportKind
{
    InProcess,
    Kafka,
}

/// <summary>
/// Sends outbox events to Kafka: key = job id, value = the event's JSON, with the event id and
/// type as headers, so a consumer can skip or dedupe without parsing the body.
/// </summary>
/// <remarks>
/// The producer is idempotent with <c>acks=all</c> (configured where it's registered): a retry
/// inside the client can't write the same message twice, and "sent" means every in-sync replica
/// has it. What the client can't cover, a crash after sending but before the outbox row is
/// marked, the consumers cover by event id.
/// </remarks>
public sealed class KafkaTransport(IProducer<string, string> producer, IOptions<EventOptions> options) : IEventTransport
{
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await producer.ProduceAsync(options.Value.Topic, new Message<string, string>
        {
            Key = message.JobId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Value = message.Payload,
            Timestamp = new Timestamp(message.OccurredAt),
            Headers = new Headers
            {
                { "event-id", Encoding.UTF8.GetBytes(message.EventId.ToString()) },
                { "event-type", Encoding.UTF8.GetBytes(message.Type) },
            },
        }, cancellationToken);
    }
}

/// <summary>
/// Creates the jobs topic with the partition count we chose, instead of leaving it to the
/// broker's auto-create defaults. Safe to run on every start: an existing topic is left alone.
/// </summary>
public static class KafkaTopics
{
    public static async Task EnsureAsync(string bootstrapServers, EventOptions options, ILogger logger)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = options.Topic, NumPartitions = options.Partitions, ReplicationFactor = 1 }]);
            logger.LogInformation("Created Kafka topic {Topic} with {Partitions} partitions", options.Topic, options.Partitions);
        }
        catch (CreateTopicsException exception) when (exception.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            // Already there from an earlier run.
        }
        catch (KafkaException exception)
        {
            // The broker isn't up yet. Don't take the API down with it: the outbox holds every
            // event until it is, and the first send creates the topic with the broker's defaults.
            logger.LogWarning(exception, "Couldn't create Kafka topic {Topic}; events wait in the outbox until the broker is back", options.Topic);
        }
    }
}
