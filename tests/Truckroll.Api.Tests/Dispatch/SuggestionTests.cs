using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Truckroll.Api.Features.Dispatch;
using Truckroll.Api.Features.Dispatch.Suggestions;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Dispatch;

/// <summary>Suggestions through the API, and the model's limited part in them.</summary>
public sealed class SuggestionTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_emergency_at_14_Maple_St_gets_only_licensed_free_technicians()
    {
        var jobId = await WithDbAsync(db => db.Jobs.Where(j => j.Property.Street == "14 Maple St" && j.Priority == JobPriority.Emergency && (j.Status == JobStatus.Scheduled || j.Status == JobStatus.Dispatched)).Select(j => j.Id).FirstAsync(Ct));
        using var alex = await factory.SignedInAsAsync(SignedInClients.GuelphDispatcher);

        var suggestions = await alex.GetFromJsonAsync<SuggestionsResponse>($"/api/jobs/{jobId}/suggestions", JsonOptions.Api, Ct);

        Assert.NotNull(suggestions);
        Assert.True(suggestions.CanAssign);
        Assert.InRange(suggestions.Top.Count, 1, 3);
        Assert.Equal(Enumerable.Range(1, suggestions.Top.Count), suggestions.Top.Select(s => s.Rank));
        Assert.True(suggestions.Top.Zip(suggestions.Top.Skip(1)).All(pair => pair.First.Score >= pair.Second.Score));

        var licensed = await WithDbAsync(db => db.Technicians.Where(t => (t.Certifications & Certifications.GasG2) != 0).Select(t => t.Id).ToListAsync(Ct));
        Assert.All(suggestions.Top, s => Assert.Contains(s.TechnicianId, licensed));
        Assert.Contains(suggestions.RuledOut, r => r.Reason == "No G2 gas licence");
        Assert.All(suggestions.Top, s => Assert.False(string.IsNullOrWhiteSpace(s.Sentence)));
    }

    [Fact]
    public async Task Asking_twice_gives_the_same_order()
    {
        var jobId = await WaitingJobAsync("KIT");
        using var pat = await factory.SignedInAsAsync(SignedInClients.KitchenerManager);

        var first = await pat.GetFromJsonAsync<SuggestionsResponse>($"/api/jobs/{jobId}/suggestions", JsonOptions.Api, Ct);
        var second = await pat.GetFromJsonAsync<SuggestionsResponse>($"/api/jobs/{jobId}/suggestions", JsonOptions.Api, Ct);

        Assert.Equal(first!.Top.Select(t => (t.TechnicianId, t.Score)), second!.Top.Select(t => (t.TechnicianId, t.Score)));
    }

    [Fact]
    public async Task Explanations_come_back_in_the_rankings_order()
    {
        var jobId = await WaitingJobAsync("KIT");
        using var pat = await factory.SignedInAsAsync(SignedInClients.KitchenerManager);
        var ranked = await pat.GetFromJsonAsync<SuggestionsResponse>($"/api/jobs/{jobId}/suggestions", JsonOptions.Api, Ct);

        var response = await pat.PostAsync($"/api/jobs/{jobId}/suggestions/explain", null, Ct);
        var explained = await response.Content.ReadFromJsonAsync<ExplanationsResponse>(JsonOptions.Api, Ct);

        Assert.Equal(ranked!.Top.Select(t => t.TechnicianId), explained!.Explanations.Select(e => e.TechnicianId));
    }

    [Fact]
    public async Task Confirming_a_suggestion_assigns_the_job()
    {
        var jobId = await WaitingJobAsync("KIT", skip: 1);
        using var pat = await factory.SignedInAsAsync(SignedInClients.KitchenerManager);
        var best = (await pat.GetFromJsonAsync<SuggestionsResponse>($"/api/jobs/{jobId}/suggestions", JsonOptions.Api, Ct))!.Top[0];

        var response = await pat.PostAsJsonAsync("/api/dispatch/assign", new { jobId, technicianId = best.TechnicianId }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId, Ct));
        Assert.Equal(best.TechnicianId, job.TechnicianId);
        Assert.Equal(JobStatus.Dispatched, job.Status);
    }

    [Fact]
    public async Task A_finished_job_gets_no_suggestions()
    {
        var jobId = await WithDbAsync(db => db.Jobs.Where(j => j.Status == JobStatus.Completed && j.Branch.Code == "KIT").Select(j => j.Id).FirstAsync(Ct));
        using var pat = await factory.SignedInAsAsync(SignedInClients.KitchenerManager);

        var response = await pat.GetAsync($"/api/jobs/{jobId}/suggestions", Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_model_cannot_reorder_add_or_drop_anyone()
    {
        var top = new[] { Scored(11, 1), Scored(22, 2), Scored(33, 3) };
        // Reversed, with someone who isn't on the list, and nothing usable for 22.
        factory.Chat.Reply("explain-order-6b1", """
            {"sentences":[
              {"technicianId":33,"sentence":"Closest and free all morning."},
              {"technicianId":99,"sentence":"Send me instead."},
              {"technicianId":22,"sentence":"   "},
              {"technicianId":11,"sentence":"Does boilers and is nearby."}
            ]}
            """);
        var explainer = new SuggestionExplainer(factory.Services.GetRequiredService<IChatClient>(), NullLogger<SuggestionExplainer>.Instance);

        var explanations = await explainer.ExplainAsync(top, ["rules 11", "rules 22", "rules 33"], "Repair for a boiler explain-order-6b1", Ct);

        Assert.Equal([11, 22, 33], explanations.Select(e => e.TechnicianId));
        Assert.Equal(["Does boilers and is nearby.", "rules 22", "Closest and free all morning."], explanations.Select(e => e.Sentence));
        Assert.Equal(["model", "rules", "model"], explanations.Select(e => e.WrittenBy));
    }

    [Fact]
    public async Task Without_a_usable_answer_every_sentence_comes_from_the_rules()
    {
        factory.Chat.Reply("explain-garbage-2d4", "Sure! Here are some thoughts about your technicians.");
        var explainer = new SuggestionExplainer(factory.Services.GetRequiredService<IChatClient>(), NullLogger<SuggestionExplainer>.Instance);

        var explanations = await explainer.ExplainAsync([Scored(1, 1), Scored(2, 2)], ["rules 1", "rules 2"], "Repair explain-garbage-2d4", Ct);

        Assert.Equal(["rules 1", "rules 2"], explanations.Select(e => e.Sentence));
        Assert.All(explanations, e => Assert.Equal("rules", e.WrittenBy));
    }

    private static ScoredTechnician Scored(int id, int rank)
    {
        var now = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.FromHours(-5));
        return new ScoredTechnician(rank, id, $"Tech {id}", 1.0 / rank, new ScoreParts(1, 1, 1, 1), new Facts(true, 2, "the Guelph branch", 0, now, now, 0, null));
    }

    /// <summary>A waiting job in a branch the dispatch board tests don't touch.</summary>
    private Task<int> WaitingJobAsync(string branch, int skip = 0) =>
        WithDbAsync(db => db.Jobs
            .Where(j => j.Branch.Code == branch && j.Status == JobStatus.Scheduled && j.TechnicianId == null)
            .OrderBy(j => j.Id).Skip(skip).Select(j => j.Id).FirstAsync(Ct));

    private async Task<T> WithDbAsync<T>(Func<TruckrollDbContext, Task<T>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TruckrollDbContext>());
    }
}
