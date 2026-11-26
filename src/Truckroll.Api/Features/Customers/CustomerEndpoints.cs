using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Truckroll.Api.Access;
using Truckroll.Api.Common;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Customers;

/// <summary>
/// Customer records: the list, the quick find behind the command palette, and the Customer 360.
/// Every query starts from <see cref="AccessScope"/>, so each role sees exactly its own slice.
/// </summary>
public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder routes)
    {
        var customers = routes.MapGroup("/customers").WithTags("Customers");

        customers.MapGet("/", ListAsync)
            .WithSummary("Customers the signed-in person may see, searched, filtered, sorted and paged.");

        customers.MapGet("/{id:int}", GetCustomerAsync)
            .WithSummary("The Customer 360: homes, equipment and warranty, membership, history and stats.");

        customers.MapGet("/search", SearchAsync)
            .WithSummary("Quick find by name, phone or street, for the command palette.");

        return routes;
    }

    // ------------------------------------------------------------------ list

    internal static async Task<Ok<CustomerPage>> ListAsync(
        [AsParameters] CustomerListQuery query, AccessScope scope, TruckrollDbContext db, CancellationToken cancellationToken)
    {
        var customers = MatchWords(scope.Customers(db), query.Q);

        if (!string.IsNullOrWhiteSpace(query.Branch))
        {
            customers = customers.Where(c => c.Branch.Code == query.Branch);
        }

        customers = query.Membership switch
        {
            "active" => customers.Where(c => c.Membership != null && c.Membership.Status == MembershipStatus.Active),
            "lapsed" => customers.Where(c => c.Membership != null && c.Membership.Status != MembershipStatus.Active),
            "none" => customers.Where(c => c.Membership == null),
            _ => customers,
        };

        var total = await customers.CountAsync(cancellationToken);

        // The scope again for the per-row job numbers, so a technician's list counts only their visits.
        var jobs = scope.Jobs(db);
        var rows = customers.Select(c => new
        {
            Customer = c,
            LastVisit = jobs.Where(j => j.CustomerId == c.Id && j.CompletedAt != null).Max(j => (DateTimeOffset?)j.CompletedAt),
            OpenJobs = jobs.Count(j => j.CustomerId == c.Id && j.Status != JobStatus.Completed && j.Status != JobStatus.Cancelled),
            Equipment = c.Properties.SelectMany(p => p.Equipment).Count(),
        });

        rows = query.Sort switch
        {
            "lastVisit" => rows.OrderByDescending(r => r.LastVisit).ThenBy(r => r.Customer.LastName),
            "equipment" => rows.OrderByDescending(r => r.Equipment).ThenBy(r => r.Customer.LastName),
            "open" => rows.OrderByDescending(r => r.OpenJobs).ThenBy(r => r.Customer.LastName),
            _ => rows.OrderBy(r => r.Customer.LastName).ThenBy(r => r.Customer.FirstName),
        };

        var pageSize = Math.Clamp(query.PageSize ?? 25, 5, 100);
        var page = Math.Max(query.Page ?? 1, 1);

        var items = await rows
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new CustomerListItem(
                r.Customer.Id,
                r.Customer.FirstName + " " + r.Customer.LastName,
                r.Customer.Phone,
                r.Customer.Properties.Select(p => p.Street + ", " + p.City).FirstOrDefault(),
                r.Customer.Branch.Code,
                r.Customer.Membership == null ? null : r.Customer.Membership.Plan,
                r.Customer.Membership == null ? null : r.Customer.Membership.Status,
                r.Equipment,
                r.LastVisit,
                r.OpenJobs))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new CustomerPage(items, total, page, pageSize));
    }

    // ------------------------------------------------------------------ the 360

    /// <summary>
    /// A customer outside the caller's scope gets 404, not 403: "not yours" would confirm the
    /// record exists, and customer ids are guessable.
    /// </summary>
    internal static async Task<Results<Ok<CustomerDetail>, NotFound>> GetCustomerAsync(
        int id, AccessScope scope, TruckrollDbContext db, CompanyClock clock, CancellationToken cancellationToken)
    {
        var customer = await scope.Customers(db)
            .Where(c => c.Id == id)
            .Include(c => c.Branch)
            .Include(c => c.Membership)
            .Include(c => c.Properties).ThenInclude(p => p.Equipment)
            .AsSplitQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return TypedResults.NotFound();
        }

        var today = clock.Today;
        var manuals = await scope.Documents(db)
            .Where(d => d.Kind == DocumentKind.Manual && d.AppliesToModel != null)
            .Select(d => new { d.Code, Prefix = d.AppliesToModel! })
            .ToListAsync(cancellationToken);

        var jobs = await scope.Jobs(db)
            .Where(j => j.CustomerId == id)
            .OrderByDescending(j => j.WindowStart)
            .Take(60)
            .Select(j => new TimelineJob(
                j.Id,
                j.Number,
                j.Type,
                j.Priority,
                j.Status,
                j.WindowStart,
                j.WindowEnd,
                j.Summary,
                j.Resolution,
                j.Technician == null ? null : j.Technician.FirstName + " " + j.Technician.LastName,
                j.InvoiceTotal,
                j.FirstTimeFix,
                j.Equipment == null ? null : j.Equipment.Brand + " " + j.Equipment.Model,
                j.Equipment == null ? null : j.Equipment.Kind,
                j.Notes.OrderBy(n => n.CreatedAt).Select(n => new TimelineNote(n.Author, n.Source, n.Body, n.CreatedAt)).ToList()))
            .ToListAsync(cancellationToken);

        var completed = jobs.Where(j => j.Status == JobStatus.Completed).ToList();
        var repairs = completed.Where(j => j.Type == JobType.Repair && j.FirstTimeFix is not null).ToList();
        var stats = new CustomerStats(
            Visits: completed.Count,
            LifetimeRevenue: completed.Sum(j => j.InvoiceTotal ?? 0),
            FirstTimeFixRate: repairs.Count == 0 ? null : Math.Round((double)repairs.Count(j => j.FirstTimeFix == true) / repairs.Count, 2),
            OpenJobs: jobs.Count(j => j.Status is not (JobStatus.Completed or JobStatus.Cancelled)));

        var properties = customer.Properties
            .OrderBy(p => p.Id)
            .Select(p => new PropertyDetail(
                p.Id,
                p.Street,
                p.City,
                p.PostalCode,
                p.Kind,
                p.YearBuilt,
                p.AccessNotes,
                p.Latitude,
                p.Longitude,
                p.Equipment
                    .OrderBy(e => e.Kind)
                    .Select(e => new EquipmentDetail(
                        e.Id,
                        e.Kind,
                        e.Brand,
                        e.Model,
                        e.SerialNumber,
                        e.InstalledOn,
                        e.AgeInYears(today),
                        e.InstalledByUs,
                        e.WarrantyExpiresOn,
                        WarrantyOf(e, today),
                        WarrantyLeft(e, today),
                        manuals.FirstOrDefault(m => e.Model.StartsWith(m.Prefix, StringComparison.Ordinal))?.Code))
                    .ToList()))
            .ToList();

        return TypedResults.Ok(new CustomerDetail(
            customer.Id,
            customer.FirstName,
            customer.LastName,
            customer.Email,
            customer.Phone,
            customer.Source,
            customer.CreatedAt,
            new BranchInfo(customer.Branch.Code, customer.Branch.Name, customer.Branch.Phone),
            customer.Membership is { } m ? new MembershipInfo(m.Plan, m.Status, m.StartedOn, m.RenewsOn, m.MonthlyPrice) : null,
            properties,
            stats,
            jobs));
    }

    /// <summary>
    /// Covered, expiring within 90 days (worth a call: a repair now is free), expired, or unknown
    /// (someone else installed it and the paperwork never turned up).
    /// </summary>
    internal static WarrantyState WarrantyOf(Equipment equipment, DateOnly today) => equipment.WarrantyExpiresOn switch
    {
        null => WarrantyState.Unknown,
        { } end when end < today => WarrantyState.Expired,
        { } end when end <= today.AddDays(90) => WarrantyState.Expiring,
        _ => WarrantyState.Covered,
    };

    /// <summary>
    /// The share of the warranty term still to run, from 1 (just installed) to 0 (expired), measured
    /// on the same company date as the warranty state so the two always agree.
    /// </summary>
    internal static double? WarrantyLeft(Equipment equipment, DateOnly today)
    {
        if (equipment.WarrantyExpiresOn is not { } end)
        {
            return null;
        }

        var term = end.DayNumber - equipment.InstalledOn.DayNumber;
        return term <= 0 ? 0 : Math.Round(Math.Clamp((double)(end.DayNumber - today.DayNumber) / term, 0, 1), 3);
    }

    // ------------------------------------------------------------------ quick find

    /// <summary>
    /// Matches every word of the query against the name, phone and street, so "maple raman" and
    /// "555-0142" both find Priya Raman. Capped at eight: it feeds a palette, not a report.
    /// </summary>
    internal static async Task<Ok<List<CustomerSearchResult>>> SearchAsync(
        string? q, AccessScope scope, TruckrollDbContext db, CancellationToken cancellationToken)
    {
        if ((q ?? "").Replace(" ", "", StringComparison.Ordinal).Length < 2)
        {
            return TypedResults.Ok(new List<CustomerSearchResult>());
        }

        var results = await MatchWords(scope.Customers(db), q)
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

    /// <summary>Every word must match the first name, last name, phone or a street.</summary>
    private static IQueryable<Customer> MatchWords(IQueryable<Customer> customers, string? q)
    {
        foreach (var word in (q ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(4))
        {
            customers = customers.Where(c =>
                c.FirstName.Contains(word)
                || c.LastName.Contains(word)
                || c.Phone.Contains(word)
                || c.Properties.Any(p => p.Street.Contains(word)));
        }

        return customers;
    }
}

/// <summary>Query string for the customer list. Everything optional.</summary>
public sealed record CustomerListQuery(string? Q, string? Branch, string? Membership, string? Sort, int? Page, int? PageSize);

public sealed record CustomerPage(List<CustomerListItem> Items, int Total, int Page, int PageSize);

public sealed record CustomerListItem(
    int Id,
    string Name,
    string Phone,
    string? Address,
    string BranchCode,
    MembershipPlan? Plan,
    MembershipStatus? MembershipStatus,
    int Equipment,
    DateTimeOffset? LastVisit,
    int OpenJobs);

/// <summary>One quick-find hit: enough to recognise the right customer at a glance.</summary>
public sealed record CustomerSearchResult(int Id, string Name, string? Address, string BranchCode);

public sealed record CustomerDetail(
    int Id,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    CustomerSource Source,
    DateTimeOffset CustomerSince,
    BranchInfo Branch,
    MembershipInfo? Membership,
    List<PropertyDetail> Properties,
    CustomerStats Stats,
    List<TimelineJob> Timeline);

public sealed record BranchInfo(string Code, string Name, string Phone);

public sealed record MembershipInfo(MembershipPlan Plan, MembershipStatus Status, DateOnly StartedOn, DateOnly RenewsOn, decimal MonthlyPrice);

public sealed record PropertyDetail(
    int Id,
    string Street,
    string City,
    string PostalCode,
    PropertyKind Kind,
    int? YearBuilt,
    string? AccessNotes,
    double Latitude,
    double Longitude,
    List<EquipmentDetail> Equipment);

public sealed record EquipmentDetail(
    int Id,
    EquipmentKind Kind,
    string Brand,
    string Model,
    string SerialNumber,
    DateOnly InstalledOn,
    int AgeYears,
    bool InstalledByUs,
    DateOnly? WarrantyExpiresOn,
    WarrantyState Warranty,
    double? WarrantyLeft,
    string? ManualCode);

public enum WarrantyState
{
    Covered,
    Expiring,
    Expired,
    Unknown,
}

public sealed record CustomerStats(int Visits, decimal LifetimeRevenue, double? FirstTimeFixRate, int OpenJobs);

public sealed record TimelineJob(
    int Id,
    string Number,
    JobType Type,
    JobPriority Priority,
    JobStatus Status,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    string Summary,
    string? Resolution,
    string? Technician,
    decimal? InvoiceTotal,
    bool? FirstTimeFix,
    string? Equipment,
    EquipmentKind? EquipmentKind,
    List<TimelineNote> Notes);

public sealed record TimelineNote(string Author, NoteSource Source, string Body, DateTimeOffset CreatedAt);
