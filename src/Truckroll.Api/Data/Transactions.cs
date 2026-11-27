using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Truckroll.Data;

namespace Truckroll.Api.Data;

/// <summary>Transactions that compose: an inner unit of work joins an outer one instead of nesting.</summary>
public static class Transactions
{
    /// <summary>
    /// Starts a READ COMMITTED transaction, or joins the one already open on this context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Booking a job is a unit of work on its own (the booking form) and part of a bigger one
    /// (approving a call also creates the customer and closes the call). With this, the inner
    /// step commits only when it owns the transaction, and the outer step decides otherwise.
    /// </para>
    /// <para>
    /// READ COMMITTED rather than MySQL's REPEATABLE READ: booking numbers jobs as MAX + 1 and
    /// retries on a clash, and under a frozen snapshot every retry reads the same stale maximum.
    /// </para>
    /// </remarks>
    public static async Task<UnitOfWork> BeginOrJoinTransactionAsync(this TruckrollDbContext db, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return new UnitOfWork(null);
        }

        return new UnitOfWork(await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken));
    }
}

/// <summary>
/// A transaction this code may or may not own. <see cref="CommitAsync"/> commits only an owned
/// one; disposing without committing rolls an owned one back.
/// </summary>
public sealed class UnitOfWork(IDbContextTransaction? owned) : IAsyncDisposable
{
    public Task CommitAsync(CancellationToken cancellationToken) => owned?.CommitAsync(cancellationToken) ?? Task.CompletedTask;

    public ValueTask DisposeAsync() => owned?.DisposeAsync() ?? ValueTask.CompletedTask;
}
