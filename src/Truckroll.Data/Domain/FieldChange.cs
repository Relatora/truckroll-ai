namespace Truckroll.Data.Domain;

/// <summary>
/// A change a technician made in the field app, possibly hours ago with no signal, and what the
/// server did with it when it finally arrived.
/// </summary>
/// <remarks>
/// <para>
/// The id is generated on the phone when the technician taps the button, not here. A phone that
/// loses its connection mid-sync can't know whether the server got the change, so it sends it
/// again; this record means the second copy gets the first one's answer and nothing happens twice.
/// </para>
/// <para>
/// The outcome and explanation are kept so the app can show them ("Nadia has this job now; your
/// resolution was saved as a note"), and so the office can see what happened later.
/// </para>
/// </remarks>
public sealed class FieldChange
{
    /// <summary>The phone's id for the change. Unique across every device, by construction (a UUID).</summary>
    public Guid Id { get; set; }

    /// <summary>Who sent it. A replay from a different account is refused, not answered.</summary>
    public required string UserId { get; set; }

    public int JobId { get; set; }

    /// <summary><c>start</c>, <c>complete</c> or <c>note</c>.</summary>
    public required string Kind { get; set; }

    public FieldChangeOutcome Outcome { get; set; }

    /// <summary>What happened, in words for the technician. Empty when it simply applied.</summary>
    public string? Explanation { get; set; }

    /// <summary>When the technician made the change, by the phone's clock.</summary>
    public DateTimeOffset MadeAt { get; set; }

    /// <summary>When it reached the server.</summary>
    public DateTimeOffset ReceivedAt { get; set; }
}

public enum FieldChangeOutcome
{
    /// <summary>Done, exactly as the technician asked.</summary>
    Applied,

    /// <summary>The job was already where the change would have put it. Nothing to do.</summary>
    AlreadyDone,

    /// <summary>
    /// The office changed the job in the meantime and the office wins (on assignment, and on
    /// cancelling). Anything the technician wrote was kept as a note.
    /// </summary>
    Rejected,
}
