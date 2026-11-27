using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Truckroll.Api.Knowledge;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;

namespace Truckroll.Api.Tests.Knowledge;

/// <summary>
/// Search through the real endpoint, as the demo users. The fake embedder makes the vector
/// ranking crude, which is fine: these tests are about who sees what and exact identifiers,
/// not about ranking quality (that's what the eval measures, with the real model).
/// </summary>
public sealed class KnowledgeSearchTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_owner_finds_a_Kitchener_customers_boiler()
    {
        using var owner = await factory.SignedInAsAsync(SignedInClients.Owner);

        var hits = await SearchAsync(owner, "boiler at 88 Birch Cres");

        Assert.Contains(hits, h => h.Source == PassageSource.Equipment && h.Text.Contains("88 Birch Cres", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_Guelph_dispatcher_never_retrieves_another_branchs_passage()
    {
        using var alex = await factory.SignedInAsAsync(SignedInClients.GuelphDispatcher);

        var hits = await SearchAsync(alex, "boiler at 88 Birch Cres", limit: 20);

        Assert.NotEmpty(hits);
        // Guelph has a Birch Cres of its own (number 311), so look for the Kitchener address exactly.
        Assert.DoesNotContain(hits, h => h.Text.Contains("88 Birch Cres", StringComparison.Ordinal));
        var guelph = await WithDbAsync(db => db.Branches.Where(b => b.Code == "GUE").Select(b => b.Id).SingleAsync(Ct));
        Assert.All(await BranchesOfAsync(hits), branch => Assert.True(branch is null || branch == guelph, $"Got a passage from branch {branch}."));
    }

    [Fact]
    public async Task A_technician_only_retrieves_jobs_they_were_sent_to()
    {
        using var nadia = await factory.SignedInAsAsync(SignedInClients.GuelphTechnician);
        var theirs = await WithDbAsync(db => db.Jobs.Where(j => j.Technician!.FirstName == "Nadia").Select(j => j.Id).ToListAsync(Ct));

        var hits = await SearchAsync(nadia, "furnace not lighting, replaced the ignitor", limit: 20);

        var jobs = hits.Where(h => h.Source == PassageSource.Job).Select(h => int.Parse(h.Record["job:".Length..], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.NotEmpty(jobs);
        Assert.All(jobs, id => Assert.Contains(id, theirs));
    }

    [Fact]
    public async Task An_exact_job_number_comes_back_first_even_typed_in_lower_case()
    {
        var number = await WithDbAsync(db => db.Jobs.Where(j => j.Customer.LastName == "Raman").OrderBy(j => j.Id).Select(j => j.Number).FirstAsync(Ct));
        using var owner = await factory.SignedInAsAsync(SignedInClients.Owner);

        var hits = await SearchAsync(owner, $"what happened on {number.ToLowerInvariant()}?");

        Assert.Equal(PassageSource.Job, hits[0].Source);
        Assert.Contains(number, hits[0].Identifiers);
        Assert.Equal(1, hits[0].IdentifierRank);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("ignitor", "sideways")]
    public async Task A_bad_request_gets_a_validation_problem(string q, string? mode)
    {
        using var owner = await factory.SignedInAsAsync(SignedInClients.Owner);

        var response = await owner.GetAsync($"/api/knowledge/search?q={Uri.EscapeDataString(q)}{(mode is null ? "" : $"&mode={mode}")}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_needs_a_signed_in_user()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync("/api/knowledge/search?q=ignitor", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<List<SearchHit>> SearchAsync(HttpClient client, string question, int limit = 8)
    {
        await IndexReadyAsync();
        var hits = await client.GetFromJsonAsync<List<SearchHit>>($"/api/knowledge/search?q={Uri.EscapeDataString(question)}&limit={limit}", JsonOptions.Api, Ct);
        return hits!;
    }

    /// <summary>The branch of each hit's record (null for company-wide documents).</summary>
    private async Task<List<int?>> BranchesOfAsync(IEnumerable<SearchHit> hits)
    {
        var result = new List<int?>();
        foreach (var hit in hits)
        {
            var id = int.Parse(hit.Record[(hit.Record.IndexOf(':') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            result.Add(hit.Source switch
            {
                PassageSource.Job => await WithDbAsync(db => db.Jobs.Where(j => j.Id == id).Select(j => (int?)j.BranchId).SingleAsync(Ct)),
                PassageSource.Equipment => await WithDbAsync(db => db.Equipment.Where(e => e.Id == id).Select(e => (int?)e.Property.Customer.BranchId).SingleAsync(Ct)),
                _ => await WithDbAsync(db => db.KnowledgeDocuments.Where(d => d.Id == id).Select(d => d.BranchId).SingleAsync(Ct)),
            });
        }

        return result;
    }

    /// <summary>The index builds in the background at startup; every test here needs it.</summary>
    private Task IndexReadyAsync() =>
        factory.Services.GetRequiredService<KnowledgeQueue>().Ready.WaitAsync(TimeSpan.FromMinutes(2), Ct);

    private async Task<T> WithDbAsync<T>(Func<TruckrollDbContext, Task<T>> work)
    {
        await IndexReadyAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TruckrollDbContext>());
    }
}
