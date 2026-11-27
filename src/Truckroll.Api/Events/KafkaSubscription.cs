using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Truckroll.Api.Features.Jobs;

namespace Truckroll.Api.Events;

/// <summary>
/// Feeds one <see cref="IJobChangeHandler"/> from the jobs topic, as its own consumer group.
/// </summary>
/// <remarks>
/// <para>
/// <b>At least once.</b> Auto-commit is off: the offset is committed only after the handler
/// finishes, so a crash mid-event means that event comes again. Handlers are written for that.
/// </para>
/// <para>
/// <b>A failing handler retries, it doesn't skip.</b> The consumer seeks back to the event and
/// tries again after a pause, so a notification isn't lost to a database blip. Live-only
/// handlers (the board) are the exception: an update that failed is stale by the next one.
/// </para>
/// </remarks>
public sealed class KafkaSubscription(
    IJobChangeHandler handler,
    string bootstrapServers,
    EventOptions options,
    ILogger<KafkaSubscription> logger) : BackgroundService
{
    private static readonly TimeSpan RetryPause = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The consumer group. A live-only handler gets one per process, so every API instance hears
    /// every event; the rest share one per subscription, so each event is handled once.
    /// </summary>
    internal string GroupId => handler.LiveOnly
        ? $"truckroll.{handler.Subscription}.{Environment.MachineName}.{Environment.ProcessId}"
        : $"truckroll.{handler.Subscription}";

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // Consume() blocks a thread, so give the loop one of its own rather than starving the pool.
        Task.Factory.StartNew(() => Run(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task Run(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = GroupId,
            EnableAutoCommit = false,
            AutoOffsetReset = handler.LiveOnly ? AutoOffsetReset.Latest : AutoOffsetReset.Earliest,
        };
        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(options.Topic);
        logger.LogInformation("Subscribed {Subscription} to {Topic} as group {Group}", handler.Subscription, options.Topic, GroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException exception)
                {
                    logger.LogWarning(exception, "{Subscription} couldn't read from Kafka; retrying", handler.Subscription);
                    await Task.Delay(RetryPause, stoppingToken);
                    continue;
                }

                if (await HandleAsync(result, stoppingToken))
                {
                    consumer.Commit(result);
                }
                else
                {
                    // Read this event again next time round, after a pause.
                    consumer.Seek(result.TopicPartitionOffset);
                    await Task.Delay(RetryPause, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        finally
        {
            consumer.Close(); // Leave the group cleanly, so its partitions move on at once.
        }
    }

    /// <summary>True when the event is done with: handled, or not worth retrying.</summary>
    private async Task<bool> HandleAsync(ConsumeResult<string, string> result, CancellationToken cancellationToken)
    {
        JobChanged? change;
        try
        {
            change = JsonSerializer.Deserialize<JobChanged>(result.Message.Value, Features.Jobs.OutboxJobEvents.Json);
        }
        catch (JsonException exception)
        {
            // A message we can't read will never become readable. Log it and move on, rather than
            // block every event behind it.
            logger.LogError(exception, "Skipping unreadable event at {Offset}", result.TopicPartitionOffset);
            return true;
        }

        if (change is null)
        {
            return true;
        }

        try
        {
            await handler.HandleAsync(change, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "{Subscription} failed on {Type} for {Number}", handler.Subscription, change.Type, change.Number);
            return handler.LiveOnly;
        }
    }
}

/// <summary>Starts one <see cref="KafkaSubscription"/> per registered handler.</summary>
public sealed class KafkaSubscriptions(
    IEnumerable<IJobChangeHandler> handlers,
    IConfiguration configuration,
    IOptions<EventOptions> options,
    ILoggerFactory loggers) : IHostedService
{
    private readonly List<KafkaSubscription> _running = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var bootstrap = configuration.GetConnectionString("kafka")
            ?? throw new InvalidOperationException("Events:Transport is Kafka, but there's no ConnectionStrings:kafka.");

        // The topic first, with our partition count, before anyone produces or subscribes.
        await KafkaTopics.EnsureAsync(bootstrap, options.Value, loggers.CreateLogger(typeof(KafkaTopics)));

        foreach (var handler in handlers)
        {
            var subscription = new KafkaSubscription(handler, bootstrap, options.Value, loggers.CreateLogger<KafkaSubscription>());
            _running.Add(subscription);
            await subscription.StartAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(_running.Select(s => s.StopAsync(cancellationToken)));
}
