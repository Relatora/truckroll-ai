using System.Globalization;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Dispatch.Suggestions;

/// <summary>A point on the map.</summary>
public sealed record GeoPoint(double Latitude, double Longitude)
{
    private const double EarthRadiusKm = 6371;

    /// <summary>Straight-line (great-circle) distance. Roads are longer; it ranks the same way.</summary>
    public double KmTo(GeoPoint other)
    {
        static double Radians(double degrees) => degrees * Math.PI / 180;
        var dLat = Radians(other.Latitude - Latitude);
        var dLon = Radians(other.Longitude - Longitude);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + (Math.Cos(Radians(Latitude)) * Math.Cos(Radians(other.Latitude)) * Math.Pow(Math.Sin(dLon / 2), 2));
        return 2 * EarthRadiusKm * Math.Asin(Math.Sqrt(a));
    }
}

/// <summary>The job to place.</summary>
public sealed record DispatchJob(
    TechnicianSkills Skill,
    Certifications Certification,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    int EstimatedMinutes,
    GeoPoint At);

/// <summary>A job already on a technician's day: just what scheduling needs.</summary>
public sealed record Visit(string Number, DateTimeOffset WindowStart, DateTimeOffset WindowEnd, int EstimatedMinutes, GeoPoint At, string Street)
{
    /// <summary>When they're likely done: they arrive as the window opens and work the estimate.</summary>
    public DateTimeOffset LikelyDone => WindowStart.AddMinutes(EstimatedMinutes);
}

/// <summary>A technician who might go, with their whole day (in every branch).</summary>
public sealed record Candidate(
    int Id,
    string Name,
    TechnicianSkills Skills,
    Certifications Certifications,
    GeoPoint Branch,
    string BranchName,
    IReadOnlyList<Visit> Day);

/// <summary>The four parts of a score, each 0 to 1 before weighting.</summary>
public sealed record ScoreParts(double Skill, double Distance, double Fit, double Workload);

/// <summary>
/// The facts behind a score, in units a dispatcher uses. Both the rules sentence and the model's
/// sentence are written from these, and only these.
/// </summary>
public sealed record Facts(
    bool HasSkill,
    double Kilometres,
    string ComingFrom,
    int JobsThatDay,
    DateTimeOffset FreeFrom,
    DateTimeOffset Arrives,
    int MinutesLate,
    string? MakesLateFor);

public sealed record ScoredTechnician(int Rank, int TechnicianId, string Name, double Score, ScoreParts Parts, Facts Facts);

public sealed record RuledOut(int TechnicianId, string Name, string Reason);

public sealed record DispatchRanking(IReadOnlyList<ScoredTechnician> Ranked, IReadOnlyList<RuledOut> RuledOut);

/// <summary>
/// Ranks who should go to a job. Plain arithmetic over licences, skills, distance, timing and
/// workload: the same inputs always give the same order, and every number can be explained.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not a model's job (docs/adr/007). A dispatcher has to trust the order,
/// a manager has to be able to ask "why her and not him?", and a licence rule must never be
/// talked out of. The model's only part is the sentence under each name.
/// </para>
/// <para>
/// Licences and clashes are filters, not scores: a technician without a G2 never ranks for a
/// furnace, however close, and one already booked in the window can't be in two places.
/// </para>
/// </remarks>
public static class DispatchScorer
{
    public const double SkillWeight = 0.30;
    public const double DistanceWeight = 0.25;
    public const double FitWeight = 0.30;
    public const double WorkloadWeight = 0.15;

    /// <summary>At this distance or more, the distance part scores zero.</summary>
    public const double FarKm = 40;

    /// <summary>At this many jobs or more in the day, the workload part scores zero.</summary>
    public const int FullDay = 5;

    /// <summary>Getting in and out of the truck, parking, finding the door.</summary>
    public const int TravelOverheadMinutes = 10;

    /// <summary>Average speed between jobs across town and county roads.</summary>
    public const double AverageKmPerHour = 50;

    /// <summary>When the first job of the day can start, local time.</summary>
    public static readonly TimeSpan DayStarts = TimeSpan.FromHours(8);

    public static DispatchRanking Rank(DispatchJob job, IEnumerable<Candidate> candidates)
    {
        var scored = new List<(ScoredTechnician Technician, double Score)>();
        var ruledOut = new List<RuledOut>();

        foreach (var candidate in candidates)
        {
            if ((candidate.Certifications & job.Certification) != job.Certification)
            {
                ruledOut.Add(new RuledOut(candidate.Id, candidate.Name, $"No {LicenceName(job.Certification)}"));
                continue;
            }

            var clash = candidate.Day.FirstOrDefault(v => v.WindowStart < job.WindowEnd && v.WindowEnd > job.WindowStart);
            if (clash is not null)
            {
                ruledOut.Add(new RuledOut(candidate.Id, candidate.Name, $"Already has {clash.Number}, {Clock(clash.WindowStart)} to {Clock(clash.WindowEnd)}"));
                continue;
            }

            var (parts, facts) = Score(job, candidate);
            var total = (SkillWeight * parts.Skill) + (DistanceWeight * parts.Distance) + (FitWeight * parts.Fit) + (WorkloadWeight * parts.Workload);
            scored.Add((new ScoredTechnician(0, candidate.Id, candidate.Name, Math.Round(total, 4), parts, facts), Math.Round(total, 4)));
        }

        var ranked = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Technician.Facts.Kilometres)
            .ThenBy(s => s.Technician.TechnicianId)
            .Select((s, i) => s.Technician with { Rank = i + 1 })
            .ToList();

        return new DispatchRanking(ranked, ruledOut.OrderBy(r => r.Name, StringComparer.Ordinal).ToList());
    }

    private static (ScoreParts Parts, Facts Facts) Score(DispatchJob job, Candidate candidate)
    {
        // Where they'll be coming from: the job before this one today, or their branch.
        var before = candidate.Day.Where(v => v.WindowStart < job.WindowStart).MaxBy(v => v.WindowStart);
        var after = candidate.Day.Where(v => v.WindowStart > job.WindowStart).MinBy(v => v.WindowStart);
        var from = before?.At ?? candidate.Branch;
        var km = from.KmTo(job.At);

        var dayStart = new DateTimeOffset(job.WindowStart.Date + DayStarts, job.WindowStart.Offset);
        var freeFrom = before?.LikelyDone ?? dayStart;
        var arrives = freeFrom.AddMinutes(TravelMinutes(km));
        var late = (int)Math.Ceiling((arrives - job.WindowStart).TotalMinutes);
        var windowMinutes = Math.Max(1, (job.WindowEnd - job.WindowStart).TotalMinutes);

        // On time scores 1; arriving late eats into the window proportionally.
        var fit = late <= 0 ? 1.0 : Math.Max(0, 1 - (late / windowMinutes));

        // Taking this job must not wreck the next one: finishing here, then driving on.
        string? makesLateFor = null;
        if (after is not null)
        {
            var startsHere = arrives > job.WindowStart ? arrives : job.WindowStart;
            var reachesNext = startsHere.AddMinutes(job.EstimatedMinutes + TravelMinutes(job.At.KmTo(after.At)));
            var overrun = (reachesNext - after.WindowStart).TotalMinutes;
            if (overrun > 0)
            {
                fit *= Math.Max(0, 1 - (overrun / 60));
                makesLateFor = after.Number;
            }
        }

        var hasSkill = candidate.Skills.HasFlag(job.Skill);
        var parts = new ScoreParts(
            hasSkill ? 1 : 0,
            Math.Max(0, 1 - (Math.Min(km, FarKm) / FarKm)),
            Math.Round(fit, 4),
            1 - (Math.Min(candidate.Day.Count, FullDay) / (double)FullDay));

        var facts = new Facts(
            hasSkill,
            Math.Round(km, 1),
            before is null ? $"the {candidate.BranchName} branch" : before.Street,
            candidate.Day.Count,
            freeFrom,
            arrives,
            Math.Max(0, late),
            makesLateFor);

        return (parts, facts);
    }

    public static int TravelMinutes(double km) => TravelOverheadMinutes + (int)Math.Round(km / AverageKmPerHour * 60);

    /// <summary>
    /// The sentence written from the facts alone, by rule. Shown at once, and kept whenever the
    /// model's sentence is missing or unusable.
    /// </summary>
    public static string RulesSentence(ScoredTechnician technician, DispatchJob job, string skillName)
    {
        var f = technician.Facts;
        var parts = new List<string>
        {
            f.HasSkill ? $"Does {skillName} work" : $"Licensed, but {skillName} isn't one of their skills",
            $"{f.Kilometres.ToString("0.#", CultureInfo.InvariantCulture)} km from {f.ComingFrom}",
            f.MinutesLate == 0
                ? $"free by {Clock(f.FreeFrom)} and there before the window opens"
                : $"would arrive around {Clock(f.Arrives)}, {f.MinutesLate} min into the window",
            f.JobsThatDay switch { 0 => "nothing else booked that day", 1 => "1 other job that day", var n => $"{n} other jobs that day" },
        };

        if (f.MakesLateFor is not null)
        {
            parts.Add($"it would make them late for {f.MakesLateFor}");
        }

        return string.Join(", ", parts) + ".";
    }

    public static string LicenceName(Certifications certification) => certification switch
    {
        Certifications.GasG2 => "G2 gas licence",
        Certifications.GasG1 => "G1 gas licence",
        Certifications.GasG3 => "G3 gas licence",
        Certifications.Refrigeration313A => "313A refrigeration licence",
        Certifications.Plumber306A => "306A plumber's licence",
        _ => "licence",
    };

    /// <summary>"9:30", "12:00", "2:15": the way a dispatcher says it.</summary>
    public static string Clock(DateTimeOffset time)
    {
        var hour = time.Hour > 12 ? time.Hour - 12 : time.Hour;
        return $"{hour}:{time.Minute:00}";
    }
}
