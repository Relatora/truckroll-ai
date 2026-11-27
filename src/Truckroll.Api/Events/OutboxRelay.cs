using Microsoft.EntityFrameworkCore;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Events;

/// <summary>Sends one outbox event on: to the in-process handlers today, to Kafka in v0.2.</summary>
public interface IEventTransport
{
    /// <summary>Completes once the event is safely handed on; throws if it couldn't be.</summary>
    Task SendAsync(OutboxMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Moves committed events out of the outbox, oldest first, and marks each one sent.
/// </summary>
/// <remarks>
/// <para>
/// <b>At least once, in order.</b> A row is marked sent only after the transport accepts it, so a
/// crash between the two sends that event again on restart. That's the contract: consumers drop
/// events they've already handled, by event id. On a failure the relay stops rather than skip
/// ahead, so one job's "assigned" can never overtake its "booked".
/// </para>
/// <para>
/// <b>Fast without hammering the database.</b> It wakes the moment a transaction commits
/// (<see cref="OutboxSignal"/>), so the dispatch board still moves within milliseconds, and
/// otherwise looks every few seconds in case a signal was missed. After a failure it backs off,
/// doubling up to half a minute, so an outage doesn't become a retry storm.
/// </para>
/// <para>
/// One relay per deployment: two would each send the same rows. That's fine for one API
/// instance; scaling out would add a lease or <c>FOR UPDATE SKIP LOCKED</c> per partition.
/// </para>
/// </remarks>
public sealed class OutboxRelay(
    IServiceScopeFactory scopes,
    IEventTransport transport,
    OutboxSignal signal,
    EventMetrics metrics,
    TimeProvider time,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    internal const int BatchSize = 50;
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            int sent;
            try
            {
                sent = await RelayBatchAsync(stoppingToken);
                failures = 0;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                failures++;
                logger.LogWarning(exception, "Couldn't relay outbox events (failure {Failures}); retrying", failures);
                sent = 0;
            }

            if (sent == BatchSize)
            {
                continue; // More are waiting: don't sleep between full batches.
            }

            var wait = failures == 0
                ? PollInterval
                : TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, failures - 1)));
            // A commit wakes us early, except while backing off: hurrying back to a broker that's
            // down helps nobody.
            await signal.WaitAsync(wait, wakeOnSignal: failures == 0, stoppingToken);
        }
    }

    /// <summary>Sends up to one batch, in order; returns how many went. Throws at the first failure.</summary>
    internal async Task<int> RelayBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();

        var waiting = await db.OutboxMessages
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var message in waiting)
        {
            try
            {
                await transport.SendAsync(message, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                metrics.SendFailed(message.Type);
                message.Attempts++;
                message.LastError = exception.Message.Length > 500 ? exception.Message[..500] : exception.Message;
                await db.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            // One save per event: a crash now re-sends at most this one, never the whole batch.
            message.PublishedAt = time.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            metrics.Published(message.Type, message.PublishedAt.Value - message.OccurredAt);
            sent++;
        }

        return sent;
    }
}
