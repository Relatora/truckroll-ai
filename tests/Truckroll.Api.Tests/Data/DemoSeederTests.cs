using Microsoft.EntityFrameworkCore;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;
using Truckroll.Data.Seed;

namespace Truckroll.Api.Tests.Data;

/// <summary>
/// Seeds a real MySQL database, in its own schema so the other tests' rows don't count as
/// "already seeded". This is the path the API takes on first start in development.
/// </summary>
public sealed class DemoSeederTests(TruckrollApiFactory factory)
{
    [Fact]
    public async Task Seeds_an_empty_database_once_and_never_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<TruckrollDbContext>()
            .UseMySql(factory.ConnectionStringFor("truckroll_seed_test"), TruckrollDbContext.ServerVersion)
            .Options;

        await using var db = new TruckrollDbContext(options);
        await db.Database.EnsureDeletedAsync(ct);
        await db.Database.MigrateAsync(ct);

        Assert.True(await DemoSeeder.SeedIfEmptyAsync(db, new DateOnly(2026, 11, 25), cancellationToken: ct));
        var jobs = await db.Jobs.CountAsync(ct);

        Assert.False(await DemoSeeder.SeedIfEmptyAsync(db, new DateOnly(2026, 11, 25), cancellationToken: ct));
        Assert.Equal(jobs, await db.Jobs.CountAsync(ct));

        Assert.Equal(5, await db.Branches.CountAsync(ct));
        Assert.Equal(22, await db.KnowledgeDocuments.CountAsync(ct));
        Assert.True(await db.JobNotes.CountAsync(ct) > 500);
    }
}
