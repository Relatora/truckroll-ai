using System.Text.Json;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Jobs;

/// <summary>Something happened to a job: booked, assigned, started, finished, cancelled, noted.</summary>
public sealed record JobChanged(int JobId, string Number, int BranchId, JobStatus Status, int? TechnicianId, string Change, string ChangedBy, DateTimeOffset At)
{
    /// <summary>
    /// The event's identity, stamped when it's written to the outbox and the same on every
    /// delivery. A consumer that must not act twice (a drafted notification) keys on it.
    /// </summary>
    public Guid EventId { get; init; }

    /// <summary>
    /// The event's name on the wire: <c>job.booked</c>, <c>job.assigned</c>, <c>job.completed</c>.
    /// Derived from <see cref="Change"/>, which is written for people ("assigned to Nadia Kowalczyk").
    /// </summary>
    public string Type => Change switch
    {
        _ when Change.StartsWith("assigned", StringComparison.Ordinal) => "job.assigned",
        _ when Change.StartsWith("noted", StringComparison.Ordinal) => "job.noted",
        "inprogress" => "job.started",
        "booked" or "unassigned" or "dispatched" or "completed" or "cancelled" or "scheduled" => $"job.{Change}",
        _ => "job.changed",
    };
}

/// <summary>
/// Where job changes go. An endpoint calls <see cref="PublishAsync"/> inside the transaction that
/// saves the change, so the event and the change commit together or not at all (docs/adr/005).
/// </summary>
public interface IJobEvents
{
    Task PublishAsync(JobChanged change, CancellationToken cancellationToken);
}

/// <summary>A subscriber to job changes. Register as many as needed; each gets every event.</summary>
/// <remarks>
/// Delivery is at least once: after a crash or a broker hiccup the same event can arrive again.
/// A handler either doesn't mind (re-indexing a job, re-broadcasting a board update) or keys on
/// <see cref="JobChanged.EventId"/> so the second delivery changes nothing.
/// </remarks>
public interface IJobChangeHandler
{
    /// <summary>
    /// The subscription's name. With Kafka it's the consumer group, so each subscription keeps
    /// its own place in the stream and a slow one never holds the others back.
    /// </summary>
    string Subscription => GetType().Name;

    /// <summary>
    /// True for subscribers that only care about what happens from now on, in this process (the
    /// board's live updates). They skip history after a restart, and every API instance gets its
    /// own copy of each event, because each instance holds its own browser connections.
    /// </summary>
    bool LiveOnly => false;

    Task HandleAsync(JobChanged change, CancellationToken cancellationToken);
}

/// <summary>
/// Records each event as a row in the outbox, in the caller's transaction. Nothing is sent from
/// here: <see cref="Events.OutboxRelay"/> sends rows once their transaction has committed.
/// </summary>
public sealed class OutboxJobEvents(TruckrollDbContext db) : IJobEvents
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(JobChanged change, CancellationToken cancellationToken)
    {
        // Without a transaction the change has already committed on its own, and this row would
        // be a second, separate write: exactly the gap the outbox exists to close. Fail loudly,
        // so a new endpoint that forgets finds out in its first test, not in production.
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Publish job events inside the transaction that saves the change (db.BeginOrJoinTransactionAsync).");
        }

        var stamped = change with { EventId = Guid.NewGuid() };
        db.Add(new OutboxMessage
        {
            EventId = stamped.EventId,
            Type = stamped.Type,
            JobId = stamped.JobId,
            Payload = JsonSerializer.Serialize(stamped, Json),
            OccurredAt = stamped.At,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
