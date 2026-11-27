using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Truckroll.Api.Access;
using Truckroll.Api.Knowledge;

namespace Truckroll.Api.Features.Knowledge;

/// <summary>Searching what the company knows: manuals, policies, equipment and job history.</summary>
public static class KnowledgeEndpoints
{
    public const int MaxQuestionLength = 500;
    public const int MaxLimit = 20;

    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/knowledge/search", SearchAsync)
            .WithSummary("Hybrid search over the knowledge index, limited to what the caller may see.");
        return routes;
    }

    /// <param name="q">The question or search terms.</param>
    /// <param name="limit">How many passages to return (1 to 20, default 8).</param>
    /// <param name="mode"><c>hybrid</c> (default), or one ranking alone: <c>vector</c>, <c>keyword</c>, <c>identifier</c>.</param>
    internal static async Task<Results<Ok<List<SearchHit>>, ValidationProblem>> SearchAsync(
        string? q,
        int? limit,
        string? mode,
        KnowledgeSearch search,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(q))
        {
            errors["q"] = ["Type what you're looking for."];
        }
        else if (q.Length > MaxQuestionLength)
        {
            errors["q"] = [$"Keep it under {MaxQuestionLength} characters."];
        }

        if (limit is < 1 or > MaxLimit)
        {
            errors["limit"] = [$"Ask for between 1 and {MaxLimit} results."];
        }

        var searchMode = SearchMode.Hybrid;
        if (mode is not null && !Enum.TryParse(mode, ignoreCase: true, out searchMode))
        {
            errors["mode"] = ["Use hybrid, vector, keyword or identifier."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var hits = await search.SearchAsync(q!, AccessScope.FromPrincipal(user), limit ?? 8, searchMode, cancellationToken);
        return TypedResults.Ok(hits.ToList());
    }
}
