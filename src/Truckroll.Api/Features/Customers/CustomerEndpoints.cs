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

        customers.MapGet("/search", SearchAsync)
            .WithSummary("Quick find by name, phone or street, for the command palette.");

        return routes;
    }

    /// <summary>
    /// Matches every word of the query against the name, phone and street, so "maple raman" and
    /// "555-0142" both find Priya Raman. Capped at eight: it feeds a palette, not a report.
    /// </summary>
    internal static async Task<Ok<List<CustomerSearchResult>>> SearchAsync(
        string? q, AccessScope scope, TruckrollDbContext db, CancellationToken cancellationToken)
    {
        var words = (q ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0 || words.Sum(w => w.Length) < 2)
        {
            return TypedResults.Ok(new List<CustomerSearchResult>());
        }

        var query = scope.Customers(db);
        foreach (var word in words.Take(4))
        {
            query = query.Where(c =>
                c.FirstName.Contains(word)
                || c.LastName.Contains(word)
                || c.Phone.Contains(word)
                || c.Properties.Any(p => p.Street.Contains(word)));
        }

        var results = await query
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Take(8)
            .Select(c => new CustomerSearchResult(
                c.Id,
                c.FirstName + " " + c.LastName,
                c.Properties.Select(p => p.Street + ", " + p.City).FirstOrDefault(),
                c.Branch.Code))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(results);
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

/// <summary>One quick-find hit: enough to recognise the right customer at a glance.</summary>
public sealed record CustomerSearchResult(int Id, string Name, string? Address, string BranchCode);
