using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Truckroll.Api.Access;
using Truckroll.Data;

namespace Truckroll.Api.Features.Branches;

/// <summary>The branches the signed-in person can see: all five for the owner, their own otherwise.</summary>
public static class BranchEndpoints
{
    public static IEndpointRouteBuilder MapBranchEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/branches", ListAsync)
            .WithTags("Branches")
            .WithSummary("Branches in the caller's scope, for filters and switchers.");

        return routes;
    }

    internal static async Task<Ok<List<BranchListItem>>> ListAsync(AccessScope scope, TruckrollDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok(await scope.Branches(db)
            .OrderBy(b => b.Name)
            .Select(b => new BranchListItem(b.Code, b.Name))
            .ToListAsync(cancellationToken));
}

public sealed record BranchListItem(string Code, string Name);
