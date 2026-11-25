namespace Truckroll.Data.Domain;

/// <summary>
/// A note on a job: what the technician saw, what the customer said, what was ordered.
/// </summary>
/// <remarks>
/// Notes are the richest text in the system ("ignitor cracked again, third time on this unit"),
/// which makes them the best source for questions like "which furnace models keep failing?".
/// They're indexed for retrieval (#11) with contact details masked.
/// </remarks>
public sealed class JobNote
{
    public int Id { get; set; }

    public int JobId { get; set; }
    public Job Job { get; set; } = null!;

    /// <summary>Display name of whoever wrote it, kept as text so it survives staff changes.</summary>
    public required string Author { get; set; }

    public NoteSource Source { get; set; }

    public required string Body { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Where a note came from. AI-written notes are always labelled as such.</summary>
public enum NoteSource
{
    Office,
    Technician,

    /// <summary>Written by an AI client through MCP, as a draft for a person to review (#16).</summary>
    Mcp,
}
