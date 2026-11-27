using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Truckroll.Api.Access;
using Truckroll.Api.Common;
using Truckroll.Api.Features.Jobs;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Dashboard;

/// <summary>
/// The owner's numbers: how each branch is doing this period against the last, and the year by
/// month. Branch managers get the same page for their own branch.
/// </summary>
public static class DashboardEndpoints
{
    public static readonly int[] Periods = [30, 90, 365];
    public const int Months = 12;

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/dashboard", GetAsync)
            .WithTags("Dashboard")
            .WithSummary("KPIs per branch for a period and the one before it, plus 12 months of revenue and completed jobs.");
        return routes;
    }

    internal static async Task<Results<Ok<DashboardResponse>, ValidationProblem, ForbidHttpResult>> GetAsync(
        int? days, string? branch, AccessScope scope, TruckrollDbContext db, CompanyClock clock, CancellationToken cancellationToken)
    {
        if (!scope.CanSeeFinancials)
        {
            return TypedResults.Forbid();
        }

        var period = days ?? 30;
        if (!Periods.Contains(period))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["days"] = ["Use 30, 90 or 365 days."] });
        }

        // The branches in view: the scope decides, and only the owner can narrow it. A manager
        // asking for another branch simply gets their own.
        var filter = scope.IsOwner ? branch : null;
        var branches = await scope.Branches(db).AsNoTracking()
            .Where(b => string.IsNullOrEmpty(filter) || b.Code == filter)
            .OrderBy(b => b.Id)
            .Select(b => new DashboardBranch(b.Id, b.Code, b.Name))
            .ToListAsync(cancellationToken);
        var ids = branches.Select(b => b.Id).ToList();

        // Ontario days: the period ends tonight and starts N days back; the previous period is
        // the N days before that. Months reach back far enough for the 12-month charts.
        var today = clock.Today;
        var window = new DashboardWindow(today.AddDays(1 - period), today, period);
        var (from, to) = (JobEndpoints.DayBounds(window.From).Start, JobEndpoints.DayBounds(window.To).End);
        var previousFrom = JobEndpoints.DayBounds(window.From.AddDays(-period)).Start;
        var monthsFrom = JobEndpoints.DayBounds(new DateOnly(today.Year, today.Month, 1).AddMonths(1 - Months)).Start;
        var earliest = previousFrom < monthsFrom ? previousFrom : monthsFrom;

        var completed = await scope.Jobs(db).AsNoTracking()
            .Where(j => ids.Contains(j.BranchId) && j.Status == JobStatus.Completed && j.CompletedAt >= earliest && j.CompletedAt < to)
            .Select(j => new CompletedJob(j.BranchId, j.Type, j.CompletedAt!.Value, j.InvoiceTotal ?? 0, j.FirstTimeFix))
            .ToListAsync(cancellationToken);

        var dispatched = await scope.Jobs(db).AsNoTracking()
            .Where(j => ids.Contains(j.BranchId) && j.Type == JobType.Repair && j.DispatchedAt >= previousFrom && j.DispatchedAt < to)
            .Select(j => new DispatchedJob(j.BranchId, j.CreatedAt, j.DispatchedAt!.Value))
            .ToListAsync(cancellationToken);

        var members = await scope.Customers(db).AsNoTracking()
            .Where(c => ids.Contains(c.BranchId) && c.Membership != null)
            .Select(c => new MemberRow(c.BranchId, c.Membership!.Status, c.Membership.RenewsOn))
            .ToListAsync(cancellationToken);

        var math = new DashboardMath(from, to, previousFrom, today);
        return TypedResults.Ok(new DashboardResponse(
            window,
            branches,
            math.Kpis(completed, dispatched, members),
            branches.Select(b => new BranchKpis(b.Code, b.Name, math.Kpis(
                completed.Where(j => j.BranchId == b.Id).ToList(),
                dispatched.Where(j => j.BranchId == b.Id).ToList(),
                members.Where(m => m.BranchId == b.Id).ToList()))).ToList(),
            DashboardMath.ByMonth(completed, branches, today, Months)));
    }
}

/// <summary>
/// The arithmetic behind the dashboard, kept free of the database so it can be tested on fixed
/// numbers. Times are compared in UTC; months are Ontario months.
/// </summary>
public sealed class DashboardMath(DateTimeOffset from, DateTimeOffset to, DateTimeOffset previousFrom, DateOnly today)
{
    public Kpis Kpis(IReadOnlyCollection<CompletedJob> completed, IReadOnlyCollection<DispatchedJob> dispatched, IReadOnlyCollection<MemberRow> members)
    {
        var now = completed.Where(j => j.CompletedAt >= from && j.CompletedAt < to).ToList();
        var before = completed.Where(j => j.CompletedAt >= previousFrom && j.CompletedAt < from).ToList();

        return new Kpis(
            new Metric(now.Count, before.Count),
            new Metric(FirstTimeFix(now), FirstTimeFix(before)),
            new Metric(now.Sum(j => j.Invoice), before.Sum(j => j.Invoice)),
            new Metric(
                Median(dispatched.Where(j => j.DispatchedAt >= from && j.DispatchedAt < to)),
                Median(dispatched.Where(j => j.DispatchedAt >= previousFrom && j.DispatchedAt < from))),
            new Members(
                members.Count(m => m.Status == MembershipStatus.Active),
                members.Count(m => m.Status == MembershipStatus.Lapsed),
                members.Count(m => m.Status == MembershipStatus.Active && m.RenewsOn >= today && m.RenewsOn <= today.AddDays(30))));
    }

    /// <summary>Share of completed repairs fixed on the first visit, as a percentage; null with no repairs.</summary>
    public static decimal? FirstTimeFix(IEnumerable<CompletedJob> jobs)
    {
        var repairs = jobs.Where(j => j.Type == JobType.Repair && j.FirstTimeFix is not null).ToList();
        return repairs.Count == 0 ? null : Math.Round(100m * repairs.Count(j => j.FirstTimeFix == true) / repairs.Count, 1);
    }

    /// <summary>Median hours from booking to dispatch; null with nothing dispatched.</summary>
    public static decimal? Median(IEnumerable<DispatchedJob> jobs)
    {
        var hours = jobs.Select(j => (j.DispatchedAt - j.CreatedAt).TotalHours).Order().ToList();
        if (hours.Count == 0)
        {
            return null;
        }

        var middle = hours.Count / 2;
        var median = hours.Count % 2 == 1 ? hours[middle] : (hours[middle - 1] + hours[middle]) / 2;
        return Math.Round((decimal)median, 1);
    }

    /// <summary>Revenue and completed jobs per branch for each of the last N Ontario months, oldest first.</summary>
    public static List<MonthPoint> ByMonth(IEnumerable<CompletedJob> completed, IReadOnlyList<DashboardBranch> branches, DateOnly today, int months)
    {
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(1 - months);
        var buckets = completed
            .Select(j => (Job: j, Local: TimeZoneInfo.ConvertTime(j.CompletedAt, CompanyClock.Zone)))
            .GroupBy(x => (x.Local.Year, x.Local.Month, x.Job.BranchId))
            .ToDictionary(g => g.Key, g => (Revenue: g.Sum(x => x.Job.Invoice), Count: g.Count()));

        return Enumerable.Range(0, months).Select(i =>
        {
            var month = first.AddMonths(i);
            return new MonthPoint(
                $"{month:yyyy-MM}",
                branches.Select(b => buckets.TryGetValue((month.Year, month.Month, b.Id), out var v)
                    ? new BranchMonth(b.Code, v.Revenue, v.Count)
                    : new BranchMonth(b.Code, 0, 0)).ToList());
        }).ToList();
    }
}

public sealed record CompletedJob(int BranchId, JobType Type, DateTimeOffset CompletedAt, decimal Invoice, bool? FirstTimeFix);

public sealed record DispatchedJob(int BranchId, DateTimeOffset CreatedAt, DateTimeOffset DispatchedAt);

public sealed record MemberRow(int BranchId, MembershipStatus Status, DateOnly RenewsOn);

public sealed record DashboardResponse(DashboardWindow Window, List<DashboardBranch> Branches, Kpis Total, List<BranchKpis> ByBranch, List<MonthPoint> Months);

/// <summary>The period, as Ontario days, inclusive.</summary>
public sealed record DashboardWindow(DateOnly From, DateOnly To, int Days);

public sealed record DashboardBranch(int Id, string Code, string Name);

/// <param name="HoursToDispatch">Median hours from booking to dispatch, repairs only.</param>
public sealed record Kpis(Metric Completed, Metric FirstTimeFix, Metric Revenue, Metric HoursToDispatch, Members Members);

/// <summary>This period and the one before it. Null when there was nothing to measure.</summary>
public sealed record Metric(decimal? Current, decimal? Previous);

/// <param name="RenewingSoon">Active memberships renewing in the next 30 days.</param>
public sealed record Members(int Active, int Lapsed, int RenewingSoon);

public sealed record BranchKpis(string Code, string Name, Kpis Kpis);

public sealed record MonthPoint(string Month, List<BranchMonth> Branches);

public sealed record BranchMonth(string Code, decimal Revenue, int Completed);
