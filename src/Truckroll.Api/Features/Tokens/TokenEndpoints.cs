using Microsoft.AspNetCore.Http.HttpResults;
using Truckroll.Api.Access;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Tokens;

/// <summary>
/// A person's own personal access tokens, managed from the MCP Access page. Signed in with the
/// browser cookie: a token can't mint or revoke tokens.
/// </summary>
public static class TokenEndpoints
{
    public const int MaxNameLength = 60;

    public static IEndpointRouteBuilder MapTokenEndpoints(this IEndpointRouteBuilder routes)
    {
        var tokens = routes.MapGroup("/tokens").WithTags("MCP access");
        tokens.MapGet("/", ListAsync).WithSummary("Your personal access tokens, newest first.");
        tokens.MapPost("/", CreateAsync).WithSummary("Create a token. Its value is in this response and nowhere else, ever.");
        tokens.MapPost("/{id:int}/revoke", RevokeAsync).WithSummary("Revoke a token. It stops working on its next request.");
        return routes;
    }

    internal static async Task<Ok<List<TokenItem>>> ListAsync(AccessScope scope, PersonalTokens tokens, TimeProvider time, CancellationToken cancellationToken) =>
        TypedResults.Ok((await tokens.ListAsync(scope.UserId!, cancellationToken)).Select(t => Describe(t, time.GetUtcNow())).ToList());

    internal static async Task<Results<Created<CreatedToken>, ValidationProblem>> CreateAsync(
        CreateTokenRequest request, AccessScope scope, PersonalTokens tokens, TimeProvider time, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > MaxNameLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = [$"Name the token after where you'll use it, like \"Work laptop\" (up to {MaxNameLength} characters)."],
            });
        }

        var created = await tokens.CreateAsync(scope.UserId!, name, cancellationToken);
        if (created is not { } made)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = [$"You already have {PersonalTokens.MaxLiveTokens} live tokens. Revoke one you no longer use first."],
            });
        }

        return TypedResults.Created($"/api/tokens/{made.Record.Id}", new CreatedToken(Describe(made.Record, time.GetUtcNow()), made.Token));
    }

    internal static async Task<Results<Ok<TokenItem>, NotFound>> RevokeAsync(int id, AccessScope scope, PersonalTokens tokens, TimeProvider time, CancellationToken cancellationToken)
    {
        var revoked = await tokens.RevokeAsync(scope.UserId!, id, cancellationToken);
        return revoked is null ? TypedResults.NotFound() : TypedResults.Ok(Describe(revoked, time.GetUtcNow()));
    }

    private static TokenItem Describe(PersonalAccessToken t, DateTimeOffset now) =>
        new(t.Id, t.Name, t.Prefix, t.CreatedAt, t.ExpiresAt, t.LastUsedAt, t.RevokedAt, t.IsLive(now));
}

public sealed record CreateTokenRequest(string? Name);

public sealed record TokenItem(int Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt, bool Live);

/// <param name="Token">The plain token. Shown once; Truckroll keeps only its hash.</param>
public sealed record CreatedToken(TokenItem Item, string Token);
