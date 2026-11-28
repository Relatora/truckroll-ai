using Truckroll.Api.Features.Field;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Field;

/// <summary>The offline conflict rules, every case, with no database (docs/adr/011).</summary>
public sealed class FieldRulesTests
{
    private const int Nadia = 7;

    private static Job Job(JobStatus status, int? technicianId = Nadia, string? otherName = null) => new()
    {
        Number = "J-24051",
        Status = status,
        TechnicianId = technicianId,
        Technician = otherName is null ? null : new Technician { FirstName = otherName.Split(' ')[0], LastName = otherName.Split(' ')[1], Color = "#000" },
        Summary = "No heat.",
    };

    [Theory]
    [InlineData(JobStatus.Dispatched)]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Cancelled)]
    public void A_note_always_applies(JobStatus status)
    {
        var decision = FieldRules.Decide(FieldChangeKind.Note, Job(status, technicianId: 99, otherName: "Tyler Brooks"), Nadia);

        Assert.Equal(FieldChangeOutcome.Applied, decision.Outcome);
        Assert.Null(decision.NewStatus);
    }

    [Fact]
    public void Start_moves_a_dispatched_job_to_in_progress()
    {
        var decision = FieldRules.Decide(FieldChangeKind.Start, Job(JobStatus.Dispatched), Nadia);

        Assert.Equal(FieldChangeOutcome.Applied, decision.Outcome);
        Assert.Equal(JobStatus.InProgress, decision.NewStatus);
    }

    [Theory]
    [InlineData(JobStatus.Dispatched)]
    [InlineData(JobStatus.InProgress)]
    public void Complete_applies_from_dispatched_or_in_progress(JobStatus status)
    {
        var decision = FieldRules.Decide(FieldChangeKind.Complete, Job(status), Nadia);

        Assert.Equal(FieldChangeOutcome.Applied, decision.Outcome);
        Assert.Equal(JobStatus.Completed, decision.NewStatus);
    }

    [Theory]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.Completed)]
    public void Starting_again_is_already_done(JobStatus status)
    {
        var decision = FieldRules.Decide(FieldChangeKind.Start, Job(status), Nadia);

        Assert.Equal(FieldChangeOutcome.AlreadyDone, decision.Outcome);
        Assert.Null(decision.NewStatus);
    }

    [Fact]
    public void Completing_a_completed_job_keeps_the_second_resolution_as_a_note()
    {
        var decision = FieldRules.Decide(FieldChangeKind.Complete, Job(JobStatus.Completed), Nadia);

        Assert.Equal(FieldChangeOutcome.AlreadyDone, decision.Outcome);
        Assert.True(decision.KeepTextAsNote);
    }

    [Fact]
    public void The_office_wins_on_assignment_and_says_who_has_the_job()
    {
        var decision = FieldRules.Decide(FieldChangeKind.Complete, Job(JobStatus.Dispatched, technicianId: 99, otherName: "Tyler Brooks"), Nadia);

        Assert.Equal(FieldChangeOutcome.Rejected, decision.Outcome);
        Assert.Null(decision.NewStatus);
        Assert.True(decision.KeepTextAsNote);
        Assert.Equal("J-24051 was given to Tyler Brooks while you were offline. Your resolution was saved as a note on the job.", decision.Explanation);
    }

    [Fact]
    public void A_job_taken_off_the_day_is_explained_without_a_name()
    {
        var decision = FieldRules.Decide(FieldChangeKind.Start, Job(JobStatus.Scheduled, technicianId: null), Nadia);

        Assert.Equal(FieldChangeOutcome.Rejected, decision.Outcome);
        Assert.False(decision.KeepTextAsNote); // A start carries no text.
        Assert.Equal("J-24051 was taken off your day while you were offline.", decision.Explanation);
    }

    [Theory]
    [InlineData(FieldChangeKind.Start, false)]
    [InlineData(FieldChangeKind.Complete, true)]
    public void The_office_wins_on_cancelling(FieldChangeKind kind, bool keepsText)
    {
        var decision = FieldRules.Decide(kind, Job(JobStatus.Cancelled), Nadia);

        Assert.Equal(FieldChangeOutcome.Rejected, decision.Outcome);
        Assert.Equal(keepsText, decision.KeepTextAsNote);
        Assert.StartsWith("J-24051 was cancelled while you were offline.", decision.Explanation, StringComparison.Ordinal);
    }
}
