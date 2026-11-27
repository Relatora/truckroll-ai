using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Truckroll.Api.Ai;
using Truckroll.Api.Data;
using Truckroll.Api.Knowledge;
using Truckroll.Data;
using Truckroll.Data.Seed;

namespace Truckroll.Api.Features.Dev;

/// <summary>
/// Development-only endpoints. Not mapped in any other environment, so they can't be reached in
/// production even by mistake.
/// </summary>
public static class DevEndpoints
{
    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder routes, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            return routes;
        }

        // The demo data is built around the day it was seeded, and the database volume survives
        // restarts, so on another day the board shows a stale "today". This rebuilds it. The Aspire
        // dashboard exposes it as a "Reset demo data" button on the api resource.
        routes.MapPost("/dev/reset-demo", ResetDemoAsync)
            .WithName("ResetDemo")
            .WithSummary("Drop the database and re-seed the demo company for today (development only).")
            .ExcludeFromDescription();

        // The smoke test for a fresh machine: one short question to the chat model and one string
        // through the embedder, with timings. Each call also leaves a span in the Aspire dashboard.
        routes.MapGet("/dev/ai-check", AiCheckAsync)
            .WithName("AiCheck")
            .WithSummary("Call the chat model and the embedder once each and report what answered (development only).")
            .ExcludeFromDescription();

        // "One command indexes the seed": also a "Rebuild knowledge index" button in the dashboard.
        routes.MapPost("/dev/reindex", ReindexAsync)
            .WithName("Reindex")
            .WithSummary("Rebuild the knowledge index from MySQL and report what it took (development only).")
            .ExcludeFromDescription();

        // Whether the index has finished building (end-to-end tests wait on this after a reset).
        // A queued rebuild counts as not ready: until it starts, the old index still looks current.
        routes.MapGet("/dev/index", async (PassageIndex index, KnowledgeQueue queue) =>
                Results.Ok(new { ready = !queue.RebuildPending && await index.IsCurrentAsync(), passages = await index.CountAsync() }))
            .WithName("IndexStatus")
            .WithSummary("Whether the knowledge index is built and current, and how many passages it holds (development only).")
            .ExcludeFromDescription();

        return routes;
    }

    internal static async Task<IResult> ReindexAsync(KnowledgeQueue queue)
    {
        try
        {
            return Results.Ok(await queue.RebuildAsync());
        }
        catch (HttpRequestException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The embedding model isn't reachable",
                detail: exception.Message);
        }
    }

    internal static async Task<IResult> AiCheckAsync(
        IChatClient chat,
        IEmbeddingGenerator<string, Embedding<float>> embedder,
        IOptions<AiOptions> options,
        CancellationToken cancellationToken)
    {
        var ai = options.Value;
        try
        {
            var clock = Stopwatch.StartNew();
            var reply = await chat.GetResponseAsync(
                "In one short sentence, what does a furnace flame sensor do?",
                new ChatOptions { MaxOutputTokens = 80 },
                cancellationToken);
            var chatMilliseconds = clock.ElapsedMilliseconds;

            clock.Restart();
            var vector = await embedder.GenerateVectorAsync("Calderon CAL-80 ignitor keeps failing", cancellationToken: cancellationToken);
            var embedMilliseconds = clock.ElapsedMilliseconds;

            var chatInfo = chat.GetService<ChatClientMetadata>();
            var embedInfo = embedder.GetService<EmbeddingGeneratorMetadata>();
            return Results.Ok(new
            {
                chat = new
                {
                    provider = ai.Provider.ToString(),
                    model = reply.ModelId ?? chatInfo?.DefaultModelId,
                    reply = reply.Text,
                    milliseconds = chatMilliseconds,
                },
                embedding = new
                {
                    model = embedInfo?.DefaultModelId,
                    dimensions = vector.Length,
                    expectedDimensions = ai.EmbeddingDimensions,
                    matches = vector.Length == ai.EmbeddingDimensions,
                    milliseconds = embedMilliseconds,
                },
            });
        }
        catch (HttpRequestException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "A model service isn't reachable",
                detail: $"{exception.Message} Is Ollama running, and are {ai.ChatModel} and {ai.EmbeddingModel} pulled?");
        }
    }

    internal static async Task<IResult> ResetDemoAsync(TruckrollDbContext db, KnowledgeQueue knowledge, IConfiguration configuration, ILogger<TruckrollDbContext> logger, CancellationToken cancellationToken)
    {
        var today = DataServiceExtensions.DemoToday(configuration);

        await db.Database.EnsureDeletedAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        await DemoSeeder.SeedIfEmptyAsync(db, today, cancellationToken: cancellationToken);

        // Every record has a new id now, so the old passages point at nothing. Rebuild in the
        // background; the reset itself shouldn't wait a minute for the embedder.
        knowledge.RequestRebuild();

        logger.LogInformation("Demo data reset for {Today}", today);
        return Results.Ok(new { seededFor = today, knowledgeIndex = "rebuilding" });
    }
}
