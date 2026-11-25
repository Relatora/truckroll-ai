namespace Truckroll.Data.Domain;

/// <summary>
/// A unit of work at a property: a repair, a tune-up, an installation. The thing that gets
/// booked, dispatched, worked and invoiced.
/// </summary>
public sealed class Job
{
    public int Id { get; set; }

    /// <summary>
    /// The human-facing number, e.g. <c>J-24051</c>. People read it out over the phone and search
    /// for it, so it's unique and indexed, and retrieval matches it as an identifier.
    /// </summary>
    public required string Number { get; set; }

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public int PropertyId { get; set; }
    public Property Property { get; set; } = null!;

    /// <summary>The appliance the job is about, when there is one (a new install has none yet).</summary>
    public int? EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }

    public JobType Type { get; set; }
    public JobPriority Priority { get; set; }
    public JobStatus Status { get; set; }

    /// <summary>The skill the work needs. Dispatch scores technicians on it.</summary>
    public TechnicianSkills RequiredSkill { get; set; }

    /// <summary>A licence the work legally needs, or <see cref="Certifications.None"/>.</summary>
    public Certifications RequiredCertification { get; set; }

    /// <summary>The arrival window promised to the customer, e.g. 8:00 to 10:00.</summary>
    public DateTimeOffset WindowStart { get; set; }

    /// <inheritdoc cref="WindowStart"/>
    public DateTimeOffset WindowEnd { get; set; }

    /// <summary>How long the work should take on site. Dispatch uses it to fit jobs into a day.</summary>
    public int EstimatedMinutes { get; set; }

    public int? TechnicianId { get; set; }
    public Technician? Technician { get; set; }

    /// <summary>What the customer reported, in their words as the CSR took them down.</summary>
    public required string Summary { get; set; }

    /// <summary>What the technician found and did. Filled in when the job completes.</summary>
    public string? Resolution { get; set; }

    public decimal? InvoiceTotal { get; set; }

    /// <summary>
    /// Fixed on the first visit, without a return trip for parts. The industry's favourite
    /// quality measure, because every repeat visit is a truck roll nobody pays for.
    /// </summary>
    public bool? FirstTimeFix { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public List<JobNote> Notes { get; set; } = [];
}

public enum JobType
{
    Repair,
    Maintenance,
    Installation,
    Estimate,
}

public enum JobPriority
{
    /// <summary>No heat in winter, a burst pipe, a gas smell. Same day, before anything else.</summary>
    Emergency,

    /// <summary>Within a day or two.</summary>
    Urgent,

    Normal,
}

/// <summary>
/// Where a job is. Moves forward only: New → Scheduled → Dispatched → InProgress → Completed,
/// with Cancelled possible before completion.
/// </summary>
public enum JobStatus
{
    /// <summary>Booked, with no window yet.</summary>
    New,

    /// <summary>Has a window, no technician.</summary>
    Scheduled,

    /// <summary>Assigned to a technician.</summary>
    Dispatched,

    /// <summary>Technician is on site.</summary>
    InProgress,

    Completed,
    Cancelled,
}
