using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Truckroll.Api.Access;
using Truckroll.Data;

namespace Truckroll.Api.Features.Customers;

/// <summary>
/// Customer records. This first endpoint reads one customer; the list and the full Customer 360
/// (equipment, warranty, timeline) arrive in #7.
/// </summary>
public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder routes)
    {
        var customers = routes.MapGroup("/customers").WithTags("Customers");

        customers.MapGet("/{id:int}", GetCustomerAsync)
            .WithSummary("One customer, if the signed-in person may see them.");

        return routes;
    }

    /// <summary>
    /// A customer outside the caller's scope gets 404, not 403: "not yours" would confirm the
    /// record exists, and customer ids are guessable.
    /// </summary>
    internal static async Task<Results<Ok<CustomerSummary>, NotFound>> GetCustomerAsync(
        int id, AccessScope scope, TruckrollDbContext db, CancellationToken cancellationToken)
    {
        var customer = await scope.Customers(db)
            .Where(c => c.Id == id)
            .Select(c => new CustomerSummary(c.Id, c.FirstName + " " + c.LastName, c.Branch.Code, c.Branch.Name))
            .SingleOrDefaultAsync(cancellationToken);

        return customer is null ? TypedResults.NotFound() : TypedResults.Ok(customer);
    }
}

public sealed record CustomerSummary(int Id, string Name, string BranchCode, string BranchName);
