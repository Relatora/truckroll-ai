using Microsoft.EntityFrameworkCore;
using Truckroll.Api.Data;
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

        return routes;
    }

    internal static async Task<IResult> ResetDemoAsync(TruckrollDbContext db, IConfiguration configuration, ILogger<TruckrollDbContext> logger, CancellationToken cancellationToken)
    {
        var today = DataServiceExtensions.DemoToday(configuration);

        await db.Database.EnsureDeletedAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        await DemoSeeder.SeedIfEmptyAsync(db, today, cancellationToken: cancellationToken);

        logger.LogInformation("Demo data reset for {Today}", today);
        return Results.Ok(new { seededFor = today });
    }
}
