namespace Truckroll.Data.Domain;

/// <summary>
/// Reference text the assistant can cite: equipment manuals, warranty terms, company policies,
/// service bulletins. Stored as markdown so headings give natural passage boundaries (#11).
/// </summary>
public sealed class KnowledgeDocument
{
    public int Id { get; set; }

    /// <summary>
    /// A short identifier people use, e.g. <c>M-CAL-80</c> for a manual or <c>P-104</c> for a
    /// policy. Matched exactly by retrieval, like job and serial numbers.
    /// </summary>
    public required string Code { get; set; }

    public required string Title { get; set; }

    public DocumentKind Kind { get; set; }

    /// <summary>For manuals and bulletins: the model-number prefix they cover, e.g. <c>CAL-80</c>.</summary>
    public string? AppliesToModel { get; set; }

    /// <summary>Null for company-wide documents; set for a branch's own procedures.</summary>
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public required string Body { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public enum DocumentKind
{
    Manual,
    Warranty,
    Policy,

    /// <summary>A manufacturer's notice about a known fault, e.g. a recalled part.</summary>
    ServiceBulletin,
}
