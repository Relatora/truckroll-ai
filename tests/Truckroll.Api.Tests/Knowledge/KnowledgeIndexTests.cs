using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using StackExchange.Redis;
using Truckroll.Api.Knowledge;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;

namespace Truckroll.Api.Tests.Knowledge;

/// <summary>The index as the running API builds it: from the seeded MySQL into real Redis, with the fake embedder.</summary>
public sealed class KnowledgeIndexTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PassageIndex Index => factory.Services.GetRequiredService<PassageIndex>();

    [Fact]
    public async Task Startup_builds_the_index_from_the_seed()
    {
        await IndexReadyAsync();

        Assert.True(await Index.IsCurrentAsync());
        var (documents, equipment, jobs) = await WithDbAsync(async db =>
            (await db.KnowledgeDocuments.CountAsync(Ct), await db.Equipment.CountAsync(Ct), await db.Jobs.CountAsync(Ct)));
        Assert.True(await Index.CountAsync() >= documents + equipment + jobs, "Every document, unit and job has at least one passage.");
    }

    [Fact]
    public async Task A_manual_section_is_its_own_passage_titled_by_its_headings()
    {
        await IndexReadyAsync();
        var manual = await WithDbAsync(db => db.KnowledgeDocuments.SingleAsync(d => d.Code == "M-CAL-80", Ct));

        var passages = await Index.PassagesOfAsync(PassageBuilder.RecordFor(manual));

        var section = Assert.Single(passages, p => p["title"].EndsWith("Burners light then shut off after a few seconds", StringComparison.Ordinal));
        Assert.Contains("flame sensor", section["text"], StringComparison.Ordinal);
        Assert.Equal("Document", section["source"]);
        Assert.Equal("all", section["branch"]);
        Assert.Contains("M-CAL-80", section["ids"].Split(','));
    }

    [Fact]
    public async Task A_customers_furnace_is_indexed_without_their_phone_number()
    {
        await IndexReadyAsync();
        var furnace = await WithDbAsync(db => db.Equipment.Include(e => e.Property).ThenInclude(p => p.Customer)
            .FirstAsync(e => e.Property.Street == "14 Maple St" && e.Kind == Truckroll.Data.Domain.EquipmentKind.Furnace, Ct));

        var passage = Assert.Single(await Index.PassagesOfAsync(PassageBuilder.RecordFor(furnace)));

        Assert.Contains(furnace.SerialNumber, passage["text"], StringComparison.Ordinal);
        Assert.Contains("Priya Raman", passage["text"], StringComparison.Ordinal);
        Assert.DoesNotContain(furnace.Property.Customer.Phone, passage["text"], StringComparison.Ordinal);
        Assert.Equal(furnace.Property.CustomerId.ToString(System.Globalization.CultureInfo.InvariantCulture), passage["customer"]);
    }

    [Fact]
    public async Task A_new_note_is_searchable_seconds_later_with_contact_details_masked()
    {
        await IndexReadyAsync();
        var job = await WithDbAsync(db => db.Jobs.Where(j => j.Technician!.FirstName == "Nadia").OrderByDescending(j => j.Id).Select(j => new { j.Id, j.Number }).FirstAsync(Ct));
        using var nadia = await factory.SignedInAsAsync(SignedInClients.GuelphTechnician);

        var response = await nadia.PostAsJsonAsync($"/api/jobs/{job.Id}/notes", new { body = "Index-check-c41e: daughter's cell is 226-555-0199, call before arriving." }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var text = await EventuallyAsync(async () =>
            (await Index.PassagesOfAsync($"job:{job.Id}")).Select(p => p["text"]).FirstOrDefault(t => t.Contains("Index-check-c41e", StringComparison.Ordinal)));

        Assert.Contains("daughter's cell is [phone]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("226-555-0199", text, StringComparison.Ordinal);
        Assert.Contains(job.Number, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vectors_are_stored_in_the_layout_the_index_searches()
    {
        await IndexReadyAsync();
        var manual = await WithDbAsync(db => db.KnowledgeDocuments.SingleAsync(d => d.Code == "M-CAL-80", Ct));
        var passage = PassageBuilder.FromDocument(manual).First(p => p.Title.EndsWith("Burners light then shut off after a few seconds", StringComparison.Ordinal));
        var options = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Api.Ai.AiOptions>>().Value;

        // Search with the exact vector the passage was stored with: it must come back first.
        var vector = factory.Embedder.Embed(options.EmbeddingDocumentPrefix + passage.EmbeddingText);
        var query = new Query("*=>[KNN 3 @vector $v AS distance]")
            .AddParam("v", MemoryMarshal.AsBytes(vector.AsSpan()).ToArray())
            .SetSortBy("distance")
            .ReturnFields("title", "distance")
            .Dialect(2);
        var redis = factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        var result = await redis.FT().SearchAsync(PassageIndex.Name, query);

        Assert.Equal(passage.Title, result.Documents[0]["title"].ToString());
        Assert.True(double.Parse(result.Documents[0]["distance"].ToString(), System.Globalization.CultureInfo.InvariantCulture) < 0.001);
    }

    private Task IndexReadyAsync() => factory.Services.GetRequiredService<KnowledgeQueue>().Ready.WaitAsync(TimeSpan.FromMinutes(2), Ct);

    private async Task<T> WithDbAsync<T>(Func<TruckrollDbContext, Task<T>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TruckrollDbContext>());
    }

    /// <summary>The index updates in the background; poll for up to ten seconds.</summary>
    private static async Task<string> EventuallyAsync(Func<Task<string?>> probe)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (await probe() is { } found)
            {
                return found;
            }

            await Task.Delay(200, Ct);
        }

        throw new TimeoutException("The change never reached the index.");
    }
}
