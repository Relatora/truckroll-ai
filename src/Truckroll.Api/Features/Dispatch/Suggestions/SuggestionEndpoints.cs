using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Truckroll.Api.Access;
using Truckroll.Api.Common;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Dispatch.Suggestions;

/// <summary>
/// "Who should go?": a ranked shortlist for one job, then (separately, because it takes a model
/// a few seconds) a sentence on each. Assigning stays a person's click on the dispatch API.
/// </summary>
public static class SuggestionEndpoints
{
    public const int Shortlist = 3;

    public static IEndpointRouteBuilder MapSuggestionEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/jobs/{id:int}/suggestions", GetAsync)
            .WithTags("Dispatch")
            .WithSummary("The best three technicians for a job, ranked by code, with the facts behind each score and who was ruled out.");
        routes.MapPost("/jobs/{id:int}/suggestions/explain", ExplainAsync)
            .WithTags("Dispatch")
            .WithSummary("A model-written sentence for each of the three, in the ranking's order.");
        return routes;
    }

    internal static async Task<Results<Ok<SuggestionsResponse>, NotFound, Conflict<ProblemMessage>>> GetAsync(
        int id, AccessScope scope, TruckrollDbContext db, CancellationToken cancellationToken)
    {
        var placed = await RankAsync(id, scope, db, cancellationToken);
        if (placed is null)
        {
            return TypedResults.NotFound();
        }

        var (job, dispatchJob, ranking, colours) = placed.Value;
        if (job.Status is not (JobStatus.Scheduled or JobStatus.Dispatched))
        {
            return TypedResults.Conflict(new ProblemMessage($"{job.Number} is {job.Status.ToString().ToLowerInvariant()}; suggestions are for jobs still to be done."));
        }

        var skill = SkillName(job.RequiredSkill);
        var top = ranking.Ranked.Take(Shortlist)
            .Select(t => new Suggestion(t.Rank, t.TechnicianId, t.Name, colours[t.TechnicianId], t.Score, t.Parts, t.Facts, DispatchScorer.RulesSentence(t, dispatchJob, skill)))
            .ToList();

        return TypedResults.Ok(new SuggestionsResponse(
            job.Id, job.Number, scope.CanDispatch, job.TechnicianId, top, ranking.RuledOut.ToList(), ranking.Ranked.Count + ranking.RuledOut.Count));
    }

    internal static async Task<Results<Ok<ExplanationsResponse>, NotFound>> ExplainAsync(
        int id, AccessScope scope, TruckrollDbContext db, SuggestionExplainer explainer, IChatClient chat, CancellationToken cancellationToken)
    {
        var placed = await RankAsync(id, scope, db, cancellationToken);
        if (placed is null)
        {
            return TypedResults.NotFound();
        }

        var (job, dispatchJob, ranking, _) = placed.Value;
        var top = ranking.Ranked.Take(Shortlist).ToList();
        var skill = SkillName(job.RequiredSkill);
        var rules = top.Select(t => DispatchScorer.RulesSentence(t, dispatchJob, skill)).ToList();
        var explanations = top.Count == 0 ? [] : await explainer.ExplainAsync(top, rules, $"{job.Type} for {job.Summary}", cancellationToken);

        return TypedResults.Ok(new ExplanationsResponse(explanations.ToList(), chat.GetService<ChatClientMetadata>()?.DefaultModelId));
    }

    /// <summary>
    /// Loads the job and everyone who might go, in local time (the scorer's "day starts at 8"
    /// means 8 in Ontario), with each technician's whole day from every branch.
    /// </summary>
    private static async Task<(Job Job, DispatchJob DispatchJob, DispatchRanking Ranking, Dictionary<int, string> Colours)?> RankAsync(
        int id, AccessScope scope, TruckrollDbContext db, CancellationToken cancellationToken)
    {
        var job = await scope.Jobs(db).Include(j => j.Property).SingleOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (job is null)
        {
            return null;
        }

        // The people this user could assign: their branch's technicians (everyone, for the owner).
        var technicians = await scope.Technicians(db).Where(t => t.IsActive).Include(t => t.Branch).ToListAsync(cancellationToken);

        var start = Local(job.WindowStart);
        var dayFrom = new DateTimeOffset(start.Date, start.Offset);
        var day = await Schedules.DayOfAsync(db, technicians.Select(t => t.Id).ToList(), dayFrom, dayFrom.AddDays(1), job.Id, cancellationToken);

        var candidates = technicians.Select(t => new Candidate(
            t.Id,
            t.FullName,
            t.Skills,
            t.Certifications,
            new GeoPoint(t.Branch.Latitude, t.Branch.Longitude),
            t.Branch.Name,
            day[t.Id].Select(s => new Visit(s.Number, Local(s.WindowStart), Local(s.WindowEnd), s.EstimatedMinutes, new GeoPoint(s.Latitude, s.Longitude), s.Street)).ToList()));

        var dispatchJob = new DispatchJob(
            job.RequiredSkill, job.RequiredCertification, start, Local(job.WindowEnd), job.EstimatedMinutes, new GeoPoint(job.Property.Latitude, job.Property.Longitude));

        return (job, dispatchJob, DispatchScorer.Rank(dispatchJob, candidates), technicians.ToDictionary(t => t.Id, t => t.Color));
    }

    private static DateTimeOffset Local(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, CompanyClock.Zone);

    /// <summary>"Does furnace work", "does heat pump work": the job's skill as a dispatcher says it.</summary>
    public static string SkillName(TechnicianSkills skill) => skill switch
    {
        TechnicianSkills.Furnaces => "furnace",
        TechnicianSkills.AirConditioning => "air conditioning",
        TechnicianSkills.HeatPumps => "heat pump",
        TechnicianSkills.Boilers => "boiler",
        TechnicianSkills.WaterHeaters => "water heater",
        TechnicianSkills.Plumbing => "plumbing",
        TechnicianSkills.Drains => "drain",
        TechnicianSkills.IndoorAirQuality => "indoor air quality",
        TechnicianSkills.Installations => "installation",
        _ => "this kind of",
    };
}

/// <param name="CanAssign">Whether this user may act on the suggestions (dispatchers, managers, the owner).</param>
/// <param name="Considered">How many technicians were looked at, ranked or ruled out.</param>
public sealed record SuggestionsResponse(int JobId, string Number, bool CanAssign, int? CurrentTechnicianId, List<Suggestion> Top, List<RuledOut> RuledOut, int Considered);

/// <param name="Sentence">The rules sentence; the model's arrives separately from /explain.</param>
public sealed record Suggestion(int Rank, int TechnicianId, string Name, string Color, double Score, ScoreParts Parts, Facts Facts, string Sentence);

public sealed record ExplanationsResponse(List<Explanation> Explanations, string? Model);
