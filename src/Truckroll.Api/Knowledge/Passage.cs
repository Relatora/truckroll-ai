namespace Truckroll.Api.Knowledge;

/// <summary>Where a passage came from. Stored as a tag, so retrieval can filter or weight by it.</summary>
public enum PassageSource
{
    Document,
    Equipment,
    Job,
}

/// <summary>
/// One searchable piece of text, small enough to cite: a manual section, one furnace, one job and
/// its notes. Every field except the vector is already masked.
/// </summary>
/// <param name="Record">The record it was cut from, e.g. <c>job:812</c>. Re-indexing a record replaces all of its passages.</param>
/// <param name="Title">Where it sits, e.g. "CAL-80 manual &gt; Troubleshooting". Shown on citations and weighted in keyword search.</param>
/// <param name="Text">The passage itself.</param>
/// <param name="BranchId">The branch it belongs to, or null for company-wide documents.</param>
/// <param name="CustomerId">The customer it's about, if any.</param>
/// <param name="Identifiers">Exact-match identifiers: job, model, serial, part and document numbers, upper case.</param>
public sealed record Passage(
    string Record,
    PassageSource Source,
    string Title,
    string Text,
    int? BranchId,
    int? CustomerId,
    IReadOnlySet<string> Identifiers)
{
    /// <summary>What the embedding model sees: the title gives a section its context.</summary>
    public string EmbeddingText => $"{Title}\n\n{Text}";
}
