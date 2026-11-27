using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Truckroll.Api.Events;

/// <summary>
/// "Something may be waiting in the outbox": a doorbell from committed transactions to the relay.
/// Many rings while the relay is busy count as one, which is all it needs to know.
/// </summary>
public sealed class OutboxSignal
{
    private readonly SemaphoreSlim _bell = new(0, 1);

    public void Ring()
    {
        try
        {
            _bell.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already rung and not yet answered.
        }
    }

    /// <summary>Waits for a ring (when <paramref name="wakeOnSignal"/>) or the timeout, whichever is first.</summary>
    public async Task WaitAsync(TimeSpan timeout, bool wakeOnSignal, CancellationToken cancellationToken)
    {
        if (wakeOnSignal)
        {
            await _bell.WaitAsync(timeout, cancellationToken);
        }
        else
        {
            await Task.Delay(timeout, cancellationToken);
        }
    }
}

/// <summary>
/// Rings the <see cref="OutboxSignal"/> after every commit. Rung after, not before: a relay woken
/// before the commit would find nothing yet, go back to sleep, and leave the board a poll behind.
/// </summary>
public sealed class OutboxCommitInterceptor(OutboxSignal signal) : DbTransactionInterceptor
{
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        signal.Ring();
        return Task.CompletedTask;
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => signal.Ring();
}
