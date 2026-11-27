using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Truckroll.Api.Access;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Access;

/// <summary>
/// The scope's queries, checked against the whole demo database rather than one record: every row
/// a role gets back must satisfy the rule, and nothing that satisfies it may be missing.
/// </summary>
public sealed class AccessScopeTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(TruckrollDbContext Db, AsyncServiceScope Services)> DbAsync()
    {
        var services = factory.Services.CreateAsyncScope();
        return (services.ServiceProvider.GetRequiredService<TruckrollDbContext>(), services);
    }

    private static async Task<AccessScope> ScopeForAsync(TruckrollDbContext db, string email) =>
        AccessScope.ForUser(await db.Users.SingleAsync(u => u.Email == email, Ct));

    [Fact]
    public async Task Office_roles_see_exactly_their_branch()
    {
        var (db, services) = await DbAsync();
        await using var _ = services;
        var dana = await ScopeForAsync(db, SignedInClients.GuelphCsr);

        var visible = await dana.Customers(db).Select(c => c.Id).OrderBy(id => id).ToListAsync(Ct);
        var expected = await db.Customers.Where(c => c.BranchId == dana.BranchId).Select(c => c.Id).OrderBy(id => id).ToListAsync(Ct);

        Assert.NotEmpty(visible);
        Assert.Equal(expected, visible);
        Assert.All(await dana.Jobs(db).Select(j => j.BranchId).Distinct().ToListAsync(Ct), b => Assert.Equal(dana.BranchId, b));
    }

    [Fact]
    public async Task A_technician_sees_only_their_own_jobs_and_those_customers()
    {
        var (db, services) = await DbAsync();
        await using var _ = services;
        var nadia = await ScopeForAsync(db, SignedInClients.GuelphTechnician);

        var jobs = await nadia.Jobs(db).ToListAsync(Ct);
        var customers = await nadia.Customers(db).Select(c => c.Id).ToListAsync(Ct);

        Assert.NotEmpty(jobs);
        Assert.All(jobs, j => Assert.Equal(nadia.TechnicianId, j.TechnicianId));
        Assert.Equal(jobs.Select(j => j.CustomerId).Distinct().Order(), customers.Order());
        Assert.Empty(await nadia.Calls(db).ToListAsync(Ct));
    }

    [Fact]
    public async Task The_owner_sees_everything()
    {
        var (db, services) = await DbAsync();
        await using var _ = services;
        var morgan = await ScopeForAsync(db, SignedInClients.Owner);

        // One snapshot for both counts: other tests insert customers and jobs in parallel, and a
        // row landing between two separate counts would look like a record the owner can't see.
        await using var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, Ct);
        Assert.Equal(await db.Customers.CountAsync(Ct), await morgan.Customers(db).CountAsync(Ct));
        Assert.Equal(await db.Jobs.CountAsync(Ct), await morgan.Jobs(db).CountAsync(Ct));
    }

    [Fact]
    public async Task Equipment_follows_customer_visibility()
    {
        var (db, services) = await DbAsync();
        await using var _ = services;
        var pat = await ScopeForAsync(db, SignedInClients.KitchenerManager);

        var branches = await pat.Equipment(db).Select(e => e.Property.Customer.BranchId).Distinct().ToListAsync(Ct);

        Assert.Equal([pat.BranchId!.Value], branches);
    }

    [Fact]
    public async Task A_user_missing_their_branch_sees_nothing_rather_than_everything()
    {
        var (db, services) = await DbAsync();
        await using var _ = services;
        var broken = AccessScope.ForUser(new AppUser { DisplayName = "Misconfigured", Role = UserRole.Csr, BranchId = null });

        Assert.Equal(0, await broken.Customers(db).CountAsync(Ct));
        Assert.Equal(0, await broken.Jobs(db).CountAsync(Ct));
        Assert.Equal(0, await broken.Calls(db).CountAsync(Ct));
    }
}
