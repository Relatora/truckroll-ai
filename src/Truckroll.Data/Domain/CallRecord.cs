namespace Truckroll.Data.Domain;

/// <summary>
/// A phone call to the office, from transcript to reviewed outcome (#15).
/// </summary>
/// <remarks>
/// The model's output (summary, proposed job, draft email) is stored as a proposal next to
/// the transcript. Nothing becomes a real job or a message until a person approves it, and the
/// reviewer and time are recorded either way.
/// </remarks>
public sealed class CallRecord
{
    public int Id { get; set; }

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    /// <summary>The customer the call was matched to, if any. Proposed by the model, confirmed by a person.</summary>
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public required string Transcript { get; set; }

    public CallStatus Status { get; set; }

    /// <summary>Two or three sentences on what the caller needs.</summary>
    public string? Summary { get; set; }

    /// <summary>The proposed job as JSON, exactly as validated from the model's structured output.</summary>
    public string? ProposedJobJson { get; set; }

    public string? DraftEmailSubject { get; set; }
    public string? DraftEmailBody { get; set; }

    /// <summary>The job created when the proposal was approved.</summary>
    public int? CreatedJobId { get; set; }
    public Job? CreatedJob { get; set; }

    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}

public enum CallStatus
{
    /// <summary>Transcript in, not analysed yet.</summary>
    New,

    /// <summary>The model has proposed a summary, job and email; waiting for a person.</summary>
    AwaitingReview,

    /// <summary>A person approved (possibly after editing). The draft is in the outbox.</summary>
    Approved,

    Dismissed,
}
