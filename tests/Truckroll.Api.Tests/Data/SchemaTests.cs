using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Data;

/// <summary>
/// The schema as MySQL actually builds it: migrations apply, the mapping round-trips, and the
/// storage choices in <see cref="TruckrollDbContext"/> hold (enums as words, unique numbers).
/// </summary>
public sealed class SchemaTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrations_are_all_applied_at_startup()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(Ct));
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(Ct), m => m.EndsWith("_InitialSchema", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_job_round_trips_with_its_whole_graph()
    {
        int jobId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();
            var job = TestData.NewJob(db, suffix: "RT");
            db.Jobs.Add(job);
            await db.SaveChangesAsync(Ct);
            jobId = job.Id;
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();
            var loaded = await db.Jobs
                .Include(j => j.Customer).ThenInclude(c => c.Membership)
                .Include(j => j.Equipment)
                .Include(j => j.Technician)
                .Include(j => j.Notes)
                .SingleAsync(j => j.Id == jobId, Ct);

            Assert.Equal(JobStatus.Dispatched, loaded.Status);
            Assert.Equal(Certifications.GasG2, loaded.RequiredCertification);
            Assert.Equal(TechnicianSkills.Furnaces | TechnicianSkills.Boilers, loaded.Technician!.Skills & (TechnicianSkills.Furnaces | TechnicianSkills.Boilers));
            Assert.Equal(MembershipPlan.ComfortClubPlus, loaded.Customer.Membership!.Plan);
            Assert.Equal(289.50m, loaded.InvoiceTotal);
            Assert.Single(loaded.Notes);
        }
    }

    [Fact]
    public async Task Enums_are_stored_as_words_so_reports_read_naturally()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();
        var job = TestData.NewJob(db, suffix: "EN");
        db.Jobs.Add(job);
        await db.SaveChangesAsync(Ct);

        var stored = await db.Database
            .SqlQuery<string>($"SELECT Status AS Value FROM Jobs WHERE Id = {job.Id}")
            .SingleAsync(Ct);

        Assert.Equal("Dispatched", stored);
    }

    [Fact]
    public async Task Job_numbers_are_unique()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();
        db.Jobs.Add(TestData.NewJob(db, suffix: "UQ"));
        await db.SaveChangesAsync(Ct);

        var duplicate = TestData.NewJob(db, suffix: "UQ2");
        duplicate.Number = (await db.Jobs.OrderByDescending(j => j.Id).FirstAsync(Ct)).Number;
        db.Jobs.Add(duplicate);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }
}
