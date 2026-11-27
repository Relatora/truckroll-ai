using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Truckroll.Api.Access;
using Truckroll.Data;

namespace Truckroll.Api.Features.Notifications;

/// <summary>The messages drafted for a job's customer, for whoever can see the job.</summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/jobs/{id:int}/customer-updates", ListAsync)
            .WithSummary("Messages drafted for the customer because of this job's events. Drafts only: never sent.");
        return routes;
    }

    internal static async Task<Results<Ok<List<CustomerUpdate>>, NotFound>> ListAsync(int id, AccessScope scope, TruckrollDbContext db, CancellationToken cancellationToken)
    {
        // Access comes from the job: if you can open it, you can see what was drafted about it.
        if (!await scope.Jobs(db).AnyAsync(j => j.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var updates = await db.CustomerNotifications
            .Where(n => n.JobId == id)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new CustomerUpdate(n.Id, n.Kind, n.Subject, n.Body, n.CreatedAt))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(updates);
    }
}

public sealed record CustomerUpdate(int Id, string Kind, string Subject, string Body, DateTimeOffset CreatedAt);
