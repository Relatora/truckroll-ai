using Microsoft.EntityFrameworkCore;

namespace Truckroll.Data.Seed;

/// <summary>
/// Puts the demo company into an empty database. Used by the API at startup in development, and
/// by the eval tool, so both see exactly the same data.
/// </summary>
public static class DemoSeeder
{
    /// <summary>The seed the demo, the README screenshots and the evals all use.</summary>
    public const int DefaultSeed = 2026;

    /// <summary>
    /// The password for every demo login. Published in the README on purpose: these accounts only
    /// exist in a local demo database full of invented people.
    /// </summary>
    public const string DemoPassword = "truckroll-demo";

    /// <summary>
    /// Seeds the database if it has no branches yet. Returns false when there was already data,
    /// so a restart never duplicates or overwrites anything.
    /// </summary>
    public static async Task<bool> SeedIfEmptyAsync(TruckrollDbContext db, DateOnly today, int seed = DefaultSeed, CancellationToken cancellationToken = default)
    {
        if (await db.Branches.AnyAsync(cancellationToken))
        {
            return false;
        }

        var data = new DemoDataGenerator(seed, today).Generate();

        // The object graph carries every relationship, so one AddRange per root and one
        // SaveChanges inserts it all in dependency order, inside one transaction.
        db.Branches.AddRange(data.Branches);
        db.Technicians.AddRange(data.Technicians);
        db.Customers.AddRange(data.Customers);
        db.Jobs.AddRange(data.Jobs);
        db.KnowledgeDocuments.AddRange(data.Documents);
        db.CallRecords.AddRange(data.Calls);
        db.Users.AddRange(DemoUsers.Create(data));

        await db.SaveChangesAsync(cancellationToken);

        // The generator numbered the jobs itself, so the next booking's number has to come after them.
        await JobNumbers.CatchUpAsync(db, cancellationToken);
        return true;
    }
}
