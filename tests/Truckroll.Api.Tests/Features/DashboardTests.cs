using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Truckroll.Api.Features.Dashboard;
using Truckroll.Api.Features.Jobs;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Features;

/// <summary>The owner dashboard: who may see it, and whether its numbers are the database's numbers.</summary>
public sealed class DashboardTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_owner_sees_every_branch_and_the_totals_add_up()
    {
        using var owner = await factory.SignedInAsAsync(SignedInClients.Owner);

        var dashboard = await GetAsync(owner, "?days=90");

        // Other tests add branches of their own while this runs; the seeded five come first.
        Assert.Equal(["KIT", "GUE", "CAM", "BRA", "STR"], dashboard.Branches.Select(b => b.Code).Take(5));
        Assert.Equal(dashboard.ByBranch.Sum(b => b.Kpis.Completed.Current), dashboard.Total.Completed.Current);
        Assert.Equal(dashboard.ByBranch.Sum(b => b.Kpis.Revenue.Current), dashboard.Total.Revenue.Current);
        Assert.True(dashboard.Total.Completed.Current > 0);
        Assert.Equal(DashboardEndpoints.Months, dashboard.Months.Count);
        Assert.Equal(TruckrollApiFactory.DemoToday.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), dashboard.Months[^1].Month);
    }

    [Fact]
    public async Task A_branch_manager_sees_only_their_branch_whatever_they_ask_for()
    {
        using var pat = await factory.SignedInAsAsync(SignedInClients.KitchenerManager);

        var dashboard = await GetAsync(pat, "?branch=GUE");

        var only = Assert.Single(dashboard.Branches);
        Assert.Equal("KIT", only.Code);
        Assert.All(dashboard.Months, m => Assert.Equal("KIT", Assert.Single(m.Branches).Code));
    }

    [Theory]
    [InlineData(SignedInClients.GuelphCsr)]
    [InlineData(SignedInClients.GuelphTechnician)]
    [InlineData(SignedInClients.GuelphDispatcher)]
    public async Task People_without_access_to_financials_are_refused(string email)
    {
        using var client = await factory.SignedInAsAsync(email);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dashboard", Ct)).StatusCode);
    }

    [Fact]
    public async Task Completed_revenue_and_first_time_fix_match_a_hand_written_query()
    {
        using var owner = await factory.SignedInAsAsync(SignedInClients.Owner);
        var dashboard = await GetAsync(owner, "?days=90");
        var from = JobEndpoints.DayBounds(dashboard.Window.From).Start.UtcDateTime;
        var to = JobEndpoints.DayBounds(dashboard.Window.To).End.UtcDateTime;

        // The same question asked of MySQL directly, without EF's LINQ or the dashboard's C#.
        var rows = await WithDbAsync(db => db.Database.SqlQuery<SqlRow>($"""
            SELECT b.Code AS Code,
                   CAST(COUNT(*) AS DECIMAL(18,2)) AS Completed,
                   COALESCE(SUM(j.InvoiceTotal), 0) AS Revenue,
                   CAST(SUM(CASE WHEN j.Type = 'Repair' AND j.FirstTimeFix IS NOT NULL THEN 1 ELSE 0 END) AS DECIMAL(18,2)) AS Repairs,
                   CAST(SUM(CASE WHEN j.Type = 'Repair' AND j.FirstTimeFix = 1 THEN 1 ELSE 0 END) AS DECIMAL(18,2)) AS FixedFirst
            FROM Jobs j JOIN Branches b ON b.Id = j.BranchId
            WHERE j.Status = 'Completed' AND j.CompletedAt >= {from} AND j.CompletedAt < {to}
            GROUP BY b.Code
            """).ToListAsync(Ct));

        foreach (var branch in dashboard.ByBranch)
        {
            // A branch with nothing completed has no row (test-made branches, mostly): all zero.
            var sql = rows.SingleOrDefault(r => r.Code == branch.Code) ?? new SqlRow(branch.Code, 0, 0, 0, 0);
            Assert.Equal(sql.Completed, branch.Kpis.Completed.Current);
            Assert.Equal(sql.Revenue, branch.Kpis.Revenue.Current);
            Assert.Equal(sql.Repairs == 0 ? null : Math.Round(100m * sql.FixedFirst / sql.Repairs, 1), branch.Kpis.FirstTimeFix.Current);
        }

        Assert.All(rows, r => Assert.Contains(dashboard.ByBranch, b => b.Code == r.Code));
    }

    // ------------------------------------------------------------------ the arithmetic

    [Fact]
    public void The_median_is_the_middle_value_or_the_mean_of_the_middle_two()
    {
        var start = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        DispatchedJob After(double hours) => new(1, start, start.AddHours(hours));

        Assert.Equal(3m, DashboardMath.Median([After(1), After(3), After(50)]));
        Assert.Equal(2.5m, DashboardMath.Median([After(1), After(2), After(3), After(200)]));
        Assert.Null(DashboardMath.Median([]));
    }

    [Fact]
    public void First_time_fix_counts_repairs_only()
    {
        var at = DateTimeOffset.UtcNow;
        CompletedJob[] jobs =
        [
            new(1, JobType.Repair, at, 100, true),
            new(1, JobType.Repair, at, 100, true),
            new(1, JobType.Repair, at, 100, false),
            new(1, JobType.Maintenance, at, 100, false),
            new(1, JobType.Installation, at, 100, null),
        ];

        Assert.Equal(66.7m, DashboardMath.FirstTimeFix(jobs));
        Assert.Null(DashboardMath.FirstTimeFix([new(1, JobType.Maintenance, at, 0, true)]));
    }

    [Fact]
    public void Months_are_Ontario_months()
    {
        // 03:00 UTC on November 1 is still October 31 in Guelph.
        var lateOctober = new CompletedJob(1, JobType.Repair, new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero), 250, true);
        var branches = new List<DashboardBranch> { new(1, "GUE", "Guelph") };

        var months = DashboardMath.ByMonth([lateOctober], branches, new DateOnly(2026, 11, 25), 2);

        Assert.Equal(["2026-10", "2026-11"], months.Select(m => m.Month));
        Assert.Equal(250m, months[0].Branches[0].Revenue);
        Assert.Equal(0, months[1].Branches[0].Completed);
    }

    private sealed record SqlRow(string Code, decimal Completed, decimal Revenue, decimal Repairs, decimal FixedFirst);

    private static async Task<DashboardResponse> GetAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<DashboardResponse>($"/api/dashboard{query}", JsonOptions.Api, Ct))!;

    private async Task<T> WithDbAsync<T>(Func<TruckrollDbContext, Task<T>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TruckrollDbContext>());
    }
}
