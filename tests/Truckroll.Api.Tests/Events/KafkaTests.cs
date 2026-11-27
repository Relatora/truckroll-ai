using System.Collections.Concurrent;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.Kafka;
using Truckroll.Api.Events;
using Truckroll.Api.Features.Jobs;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Events;

/// <summary>A real Kafka broker for this class only; the rest of the suite runs events in-process.</summary>
public sealed class KafkaBroker : IAsyncLifetime
{
    // The same image the AppHost runs: Apache Kafka compiled to a native binary, quick to start.
    public KafkaContainer Container { get; } = new KafkaBuilder("apache/kafka-native:4.4.0").Build();

    public string Bootstrap => Container.GetBootstrapAddress();

    public ValueTask InitializeAsync() => new(Container.StartAsync());

    public ValueTask DisposeAsync() => Container.DisposeAsync();
}

/// <summary>
/// The promise in #20, end to end through a real broker: every event arrives, in order per job,
/// through a broker outage, and nothing a consumer does happens twice (docs/adr/009).
/// </summary>
public sealed class KafkaTests(TruckrollApiFactory factory, KafkaBroker broker) : IClassFixture<KafkaBroker>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_event_reaches_a_subscription_in_order_for_each_job()
    {
        var options = Options("order");
        await using var outbox = await OutboxAsync("kafka_order");
        await outbox.AddAsync(Change(1, "booked"), Change(2, "booked"), Change(1, "assigned to Nadia Kowalczyk"), Change(2, "assigned to Ben Okoro"), Change(1, "unassigned"));

        var recorder = new Recorder("order");
        await using var subscription = await SubscribeAsync(recorder, options);
        using var producer = Producer();
        var relay = outbox.Relay(new KafkaTransport(producer, Microsoft.Extensions.Options.Options.Create(options)));

        Assert.Equal(5, await relay.RelayBatchAsync(Ct));

        await EventuallyAsync(() => recorder.Seen.Count == 5);
        Assert.Equal(["job.booked", "job.assigned", "job.unassigned"], recorder.TypesFor(1));
        Assert.Equal(["job.booked", "job.assigned"], recorder.TypesFor(2));
    }

    [Fact]
    public async Task Nothing_is_lost_or_doubled_across_a_broker_outage()
    {
        var options = Options("outage");
        await using var outbox = await OutboxAsync("kafka_outage");
        var recorder = new Recorder("outage");
        await using var subscription = await SubscribeAsync(recorder, options);
        using var producer = Producer();
        var relay = outbox.Relay(new KafkaTransport(producer, Microsoft.Extensions.Options.Options.Create(options)));

        // The broker goes away while the office keeps working.
        await broker.Container.PauseAsync(Ct);
        await outbox.AddAsync(Enumerable.Range(1, 6).Select(job => Change(job, "booked")).ToArray());

        await Assert.ThrowsAnyAsync<KafkaException>(() => relay.RelayBatchAsync(Ct));
        Assert.Equal(6, await outbox.WaitingAsync()); // Held in MySQL, not dropped.

        // Back up: the relay drains the outbox, and every event arrives exactly once in effect.
        await broker.Container.UnpauseAsync(Ct);
        for (var tries = 0; tries < 20 && await outbox.WaitingAsync() > 0; tries++)
        {
            try
            {
                await relay.RelayBatchAsync(Ct);
            }
            catch (KafkaException)
            {
                await Task.Delay(500, Ct); // Still reconnecting.
            }
        }

        Assert.Equal(0, await outbox.WaitingAsync());
        await EventuallyAsync(() => recorder.Handled.Count == 6);
        Assert.Equal(6, recorder.Handled.Count);
        Assert.All(Enumerable.Range(1, 6), job => Assert.Equal(["job.booked"], recorder.TypesFor(job)));
    }

    [Fact]
    public async Task A_failed_event_is_retried_not_skipped_and_handled_once()
    {
        var options = Options("retry");
        await using var outbox = await OutboxAsync("kafka_retry");
        await outbox.AddAsync(Change(1, "booked"), Change(1, "assigned to Nadia Kowalczyk"), Change(1, "completed"));

        // The database blips on the second event, once.
        var recorder = new Recorder("retry", failOnceOn: "job.assigned");
        await using var subscription = await SubscribeAsync(recorder, options);
        using var producer = Producer();
        await outbox.Relay(new KafkaTransport(producer, Microsoft.Extensions.Options.Options.Create(options))).RelayBatchAsync(Ct);

        await EventuallyAsync(() => recorder.Handled.Count == 3);
        Assert.Equal(["job.booked", "job.assigned", "job.completed"], recorder.TypesFor(1));
        Assert.Equal(4, recorder.Deliveries); // The failed one came round again...
        Assert.Equal(3, recorder.Handled.Count); // ...and still counted once.
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A topic per test, so tests can't read each other's events.</summary>
    private static EventOptions Options(string name) => new() { Topic = $"test.{name}.{Guid.NewGuid():N}", Partitions = 3 };

    private IProducer<string, string> Producer() => new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers = broker.Bootstrap,
        EnableIdempotence = true,
        Acks = Acks.All,
        MessageTimeoutMs = 4_000, // Fail fast while the broker is paused.
    }).Build();

    private async Task<AsyncDisposableSubscription> SubscribeAsync(IJobChangeHandler handler, EventOptions options)
    {
        await KafkaTopics.EnsureAsync(broker.Bootstrap, options, NullLogger.Instance);
        var subscription = new KafkaSubscription(handler, broker.Bootstrap, options, NullLogger<KafkaSubscription>.Instance);
        await subscription.StartAsync(Ct);
        return new AsyncDisposableSubscription(subscription);
    }

    private async Task<TestOutbox> OutboxAsync(string database)
    {
        var services = new ServiceCollection()
            .AddDbContext<TruckrollDbContext>(o => o.UseMySql(factory.ConnectionStringFor(database), TruckrollDbContext.ServerVersion))
            .AddMetrics()
            .AddSingleton<EventMetrics>()
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();
        await db.Database.EnsureDeletedAsync(Ct);
        await db.Database.EnsureCreatedAsync(Ct);
        return new TestOutbox(services);
    }

    private static JobChanged Change(int jobId, string change) =>
        new(jobId, $"J-{jobId}", 1, JobStatus.Scheduled, null, change, "Test", DateTimeOffset.UtcNow) { EventId = Guid.NewGuid() };

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        for (var tries = 0; tries < 150 && !condition(); tries++)
        {
            await Task.Delay(100, Ct);
        }
    }

    /// <summary>An outbox on a database of its own, so the API's relay can't send these rows.</summary>
    private sealed class TestOutbox(ServiceProvider services) : IAsyncDisposable
    {
        public async Task AddAsync(params JobChanged[] changes)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();
            db.OutboxMessages.AddRange(changes.Select(c => new OutboxMessage
            {
                EventId = c.EventId,
                Type = c.Type,
                JobId = c.JobId,
                Payload = JsonSerializer.Serialize(c, OutboxJobEvents.Json),
                OccurredAt = c.At,
            }));
            await db.SaveChangesAsync(Ct);
        }

        public async Task<int> WaitingAsync()
        {
            await using var scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<TruckrollDbContext>().OutboxMessages.CountAsync(m => m.PublishedAt == null, Ct);
        }

        public OutboxRelay Relay(IEventTransport transport) =>
            new(services.GetRequiredService<IServiceScopeFactory>(), transport, new OutboxSignal(), services.GetRequiredService<EventMetrics>(), TimeProvider.System, NullLogger<OutboxRelay>.Instance);

        public ValueTask DisposeAsync() => services.DisposeAsync();
    }

    private sealed class AsyncDisposableSubscription(KafkaSubscription subscription) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await subscription.StopAsync(CancellationToken.None);
            subscription.Dispose();
        }
    }

    /// <summary>
    /// A consumer written the way real ones must be: it remembers event ids, so a redelivery is
    /// counted as a delivery but handled once.
    /// </summary>
    private sealed class Recorder(string name, string? failOnceOn = null) : IJobChangeHandler
    {
        private readonly ConcurrentQueue<JobChanged> _seen = new();
        private int _deliveries;
        private bool _failed;

        // A fresh consumer group per test, fixed for the test's lifetime.
        public string Subscription { get; } = $"test-{name}-{Guid.NewGuid():N}";

        public ConcurrentDictionary<Guid, bool> Handled { get; } = new();

        public IReadOnlyCollection<JobChanged> Seen => _seen;

        public int Deliveries => _deliveries;

        public List<string> TypesFor(int jobId) => _seen.Where(c => c.JobId == jobId).Select(c => c.Type).ToList();

        public Task HandleAsync(JobChanged change, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _deliveries);
            if (change.Type == failOnceOn && !_failed)
            {
                _failed = true;
                throw new InvalidOperationException("Database blip");
            }

            if (Handled.TryAdd(change.EventId, true))
            {
                _seen.Enqueue(change);
            }

            return Task.CompletedTask;
        }
    }
}
