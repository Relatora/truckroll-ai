namespace Truckroll.Data.Domain;

/// <summary>
/// An event waiting to leave the database: written in the same transaction as the change it
/// describes, and sent on by the relay after that transaction commits (docs/adr/005).
/// </summary>
/// <remarks>
/// This is what makes "the job was saved" and "the world hears about it" one fact instead of two.
/// Without it, a save could commit and the publish fail (a change nobody hears about), or the
/// publish succeed and the save roll back (an event about something that never happened).
/// </remarks>
public sealed class OutboxMessage
{
    /// <summary>Insert order, which is also send order: the relay reads rows by id.</summary>
    public long Id { get; set; }

    /// <summary>
    /// The event's identity, the same on every delivery. Consumers keep the ids they've handled,
    /// so a redelivered event changes nothing.
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>What happened, as a dotted name: <c>job.booked</c>, <c>job.assigned</c>.</summary>
    public required string Type { get; set; }

    /// <summary>The job it's about. Events for one job must stay in order, so this is the partition key.</summary>
    public int JobId { get; set; }

    /// <summary>The event itself, as JSON.</summary>
    public required string Payload { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Set once the relay has handed the event on. Null means it's still waiting.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>How many sends have failed so far, and why the last one did.</summary>
    public int Attempts { get; set; }

    public string? LastError { get; set; }
}
