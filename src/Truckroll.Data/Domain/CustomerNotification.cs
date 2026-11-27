namespace Truckroll.Data.Domain;

/// <summary>
/// A message drafted for a customer because something happened to their job, such as "your
/// technician is booked". Always a draft: Truckroll has no way to send it, and a person decides.
/// </summary>
/// <remarks>
/// Written by the notifications subscriber, the first consumer of the job event stream that
/// lives outside the request that made the change (docs/adr/009).
/// </remarks>
public sealed class CustomerNotification
{
    public int Id { get; set; }

    /// <summary>
    /// The event that caused it. Unique, so the same event delivered twice drafts one message.
    /// </summary>
    public Guid EventId { get; set; }

    public int JobId { get; set; }
    public Job Job { get; set; } = null!;

    public int CustomerId { get; set; }

    /// <summary>Why it was drafted, for example <c>technician-assigned</c>.</summary>
    public required string Kind { get; set; }

    public required string Subject { get; set; }
    public required string Body { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
