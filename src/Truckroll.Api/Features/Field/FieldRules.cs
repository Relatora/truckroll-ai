using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Field;

/// <summary>What a technician did in the field app.</summary>
public enum FieldChangeKind
{
    /// <summary>Arrived and started work.</summary>
    Start,

    /// <summary>Finished, with what they found and did.</summary>
    Complete,

    /// <summary>Wrote a note.</summary>
    Note,
}

/// <summary>What to do with one change from the field, decided before anything is written.</summary>
/// <param name="Outcome">Applied, already done, or rejected.</param>
/// <param name="NewStatus">The status to move the job to, when the change applies.</param>
/// <param name="Explanation">For the technician, in plain words. Null when it simply applied.</param>
/// <param name="KeepTextAsNote">
/// True when the change is rejected but carried text (a resolution): that text is saved as a
/// note, so nothing a technician typed is ever lost.
/// </param>
public sealed record FieldDecision(FieldChangeOutcome Outcome, JobStatus? NewStatus, string? Explanation, bool KeepTextAsNote);

/// <summary>
/// The conflict rules for changes made offline (docs/adr/011). A change can arrive hours after
/// it was made, and the office may have moved the job in between. Who wins:
/// <list type="bullet">
///   <item><b>Notes:</b> the technician, always. A note can't conflict with anything.</item>
///   <item><b>Assignment:</b> the office. If the job was given to someone else or taken off the
///   technician's day, their start or completion doesn't apply.</item>
///   <item><b>Cancelling:</b> the office. A cancelled job stays cancelled.</item>
/// </list>
/// When the office wins, whatever the technician wrote is kept as a note, and the explanation
/// says what happened.
/// </summary>
/// <remarks>
/// Pure: the job's state in, a decision out. The endpoint does the reading and writing, and
/// these rules can be tested exhaustively without a database.
/// </remarks>
public static class FieldRules
{
    public static FieldDecision Decide(FieldChangeKind kind, Job job, int technicianId)
    {
        if (kind == FieldChangeKind.Note)
        {
            return new(FieldChangeOutcome.Applied, null, null, false);
        }

        var carriesText = kind == FieldChangeKind.Complete;
        var saved = carriesText ? " Your resolution was saved as a note on the job." : "";

        // Office wins on assignment.
        if (job.TechnicianId != technicianId)
        {
            var why = job.Technician is { } other
                ? $"{job.Number} was given to {other.FullName} while you were offline."
                : $"{job.Number} was taken off your day while you were offline.";
            return new(FieldChangeOutcome.Rejected, null, why + saved, carriesText);
        }

        // Office wins on cancelling.
        if (job.Status == JobStatus.Cancelled)
        {
            return new(FieldChangeOutcome.Rejected, null, $"{job.Number} was cancelled while you were offline.{saved}", carriesText);
        }

        return (kind, job.Status) switch
        {
            (FieldChangeKind.Start, JobStatus.Dispatched) => new(FieldChangeOutcome.Applied, JobStatus.InProgress, null, false),
            (FieldChangeKind.Start, JobStatus.InProgress or JobStatus.Completed) => new(FieldChangeOutcome.AlreadyDone, null, null, false),

            // Completing straight from dispatched is allowed: a technician who forgot to tap
            // "start" shouldn't be stuck. The completion time is theirs either way.
            (FieldChangeKind.Complete, JobStatus.Dispatched or JobStatus.InProgress) => new(FieldChangeOutcome.Applied, JobStatus.Completed, null, false),
            (FieldChangeKind.Complete, JobStatus.Completed) => new(
                FieldChangeOutcome.AlreadyDone, null,
                $"{job.Number} was already completed. Your resolution was saved as a note on the job.", true),

            // Assigned to them but not dispatched (a state the board doesn't produce today).
            _ => new(FieldChangeOutcome.Rejected, null, $"{job.Number} isn't ready to {(kind == FieldChangeKind.Start ? "start" : "complete")} yet.{saved}", carriesText),
        };
    }
}
