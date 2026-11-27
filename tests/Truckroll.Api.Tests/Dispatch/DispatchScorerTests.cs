using Truckroll.Api.Features.Dispatch.Suggestions;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Dispatch;

/// <summary>The ranking rules, one at a time, with everything else held equal.</summary>
public sealed class DispatchScorerTests
{
    private static readonly TimeSpan Ontario = TimeSpan.FromHours(-5);
    private static readonly GeoPoint Branch = new(43.55, -80.25);

    [Fact]
    public void A_technician_without_the_licence_is_ruled_out_however_close()
    {
        var ranking = DispatchScorer.Rank(Furnace(at: Branch), [Tech(1, "Ben", certifications: Certifications.GasG3), Tech(2, "Nadia")]);

        Assert.Equal([2], ranking.Ranked.Select(r => r.TechnicianId));
        var ben = Assert.Single(ranking.RuledOut);
        Assert.Equal("No G2 gas licence", ben.Reason);
    }

    [Fact]
    public void A_technician_already_booked_in_the_window_is_ruled_out()
    {
        var busy = Tech(1, "Ben", day: [Visit("J-1", 11, 13, Branch)]);

        var ranking = DispatchScorer.Rank(Furnace(at: Branch, from: 12), [busy, Tech(2, "Nadia")]);

        Assert.Equal("Already has J-1, 11:00 to 1:00", Assert.Single(ranking.RuledOut).Reason);
    }

    [Fact]
    public void With_everything_else_equal_the_one_who_does_this_work_ranks_first()
    {
        var ranking = DispatchScorer.Rank(Furnace(at: Branch), [Tech(1, "Generalist", skills: TechnicianSkills.Plumbing), Tech(2, "Furnace tech")]);

        Assert.Equal([2, 1], ranking.Ranked.Select(r => r.TechnicianId));
        Assert.False(ranking.Ranked[1].Facts.HasSkill);
    }

    [Fact]
    public void With_everything_else_equal_the_nearer_one_ranks_first()
    {
        var near = Tech(1, "Near", day: [Visit("J-1", 8, 10, Km(2, 0))]);
        var far = Tech(2, "Far", day: [Visit("J-2", 8, 10, Km(25, 0))]);

        var ranking = DispatchScorer.Rank(Furnace(at: Km(3, 0), from: 12), [far, near]);

        Assert.Equal([1, 2], ranking.Ranked.Select(r => r.TechnicianId));
        Assert.Equal(1.0, ranking.Ranked[0].Facts.Kilometres, precision: 1);
    }

    [Fact]
    public void Arriving_late_costs_more_than_a_longer_drive()
    {
        // Late: a long job from 8:00 runs to 11:30, for a window opening at 10:00.
        var late = Tech(1, "Late", day: [Visit("J-1", 8, 10, Km(2, 0), estimatedMinutes: 210)]);
        var onTime = Tech(2, "On time", day: [Visit("J-2", 8, 9, Km(12, 0))]);

        var ranking = DispatchScorer.Rank(Furnace(at: Km(1, 0), from: 10), [late, onTime]);

        Assert.Equal(2, ranking.Ranked[0].TechnicianId);
        Assert.True(ranking.Ranked[1].Facts.MinutesLate > 60);
    }

    [Fact]
    public void A_job_that_would_make_them_late_for_their_next_one_says_so_and_scores_lower()
    {
        // Free all morning, but due across the county at 1:00.
        var squeezed = Tech(1, "Squeezed", day: [Visit("J-9", 13, 15, Km(38, 0))]);
        var free = Tech(2, "Free");

        var ranking = DispatchScorer.Rank(Furnace(at: Branch, from: 11, minutes: 90), [squeezed, free]);

        var first = ranking.Ranked[0];
        var second = ranking.Ranked[1];
        Assert.Equal(2, first.TechnicianId);
        Assert.Equal("J-9", second.Facts.MakesLateFor);
        Assert.True(second.Parts.Fit < 1);
    }

    [Fact]
    public void A_busier_day_ranks_lower_when_nothing_else_differs()
    {
        GeoPoint home = Km(0, 0);
        var busy = Tech(1, "Busy", day: [Visit("J-1", 8, 9, home, 30), Visit("J-2", 14, 15, home, 30), Visit("J-3", 15, 16, home, 30)]);
        var light = Tech(2, "Light", day: [Visit("J-4", 8, 9, home, 30)]);

        var ranking = DispatchScorer.Rank(Furnace(at: home, from: 10), [busy, light]);

        Assert.Equal([2, 1], ranking.Ranked.Select(r => r.TechnicianId));
    }

    [Fact]
    public void The_order_never_depends_on_the_order_candidates_arrive_in()
    {
        Candidate[] team = [Tech(5, "E"), Tech(3, "C"), Tech(1, "A"), Tech(4, "D"), Tech(2, "B")];

        var forward = DispatchScorer.Rank(Furnace(at: Branch), team).Ranked.Select(r => r.TechnicianId);
        var backward = DispatchScorer.Rank(Furnace(at: Branch), team.Reverse()).Ranked.Select(r => r.TechnicianId);

        // All identical: ties go to the lower id.
        Assert.Equal([1, 2, 3, 4, 5], forward);
        Assert.Equal(forward, backward);
    }

    [Fact]
    public void The_rules_sentence_says_what_the_numbers_say()
    {
        var job = Furnace(at: Km(4, 0), from: 12);
        var nadia = Tech(1, "Nadia", day: [Visit("J-1", 9, 11, Km(1, 0)), Visit("J-2", 15, 16, Km(1, 0))]);

        var top = DispatchScorer.Rank(job, [nadia]).Ranked[0];

        Assert.Equal(
            "Does furnace work, 3 km from 1 Test St, free by 10:30 and there before the window opens, 2 other jobs that day.",
            DispatchScorer.RulesSentence(top, job, "furnace"));
    }

    // ------------------------------------------------------------------ builders

    private static GeoPoint Km(double east, double north) =>
        new(Branch.Latitude + (north / 111.0), Branch.Longitude + (east / (111.0 * Math.Cos(Branch.Latitude * Math.PI / 180))));

    private static DateTimeOffset At(int hour) => new(2026, 12, 1, hour, 0, 0, Ontario);

    private static DispatchJob Furnace(GeoPoint at, int from = 12, int minutes = 90) =>
        new(TechnicianSkills.Furnaces, Certifications.GasG2, At(from), At(from + 2), minutes, at);

    private static Visit Visit(string number, int from, int to, GeoPoint at, int estimatedMinutes = 90) =>
        new(number, At(from), At(to), estimatedMinutes, at, "1 Test St");

    private static Candidate Tech(
        int id,
        string name,
        TechnicianSkills skills = TechnicianSkills.Furnaces,
        Certifications certifications = Certifications.GasG3 | Certifications.GasG2,
        Visit[]? day = null) =>
        new(id, name, skills, certifications, Branch, "Guelph", day ?? []);
}
