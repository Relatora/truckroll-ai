using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Truckroll.Api.Access;
using Truckroll.Api.Common;
using Truckroll.Api.Data;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Jobs;

/// <summary>
/// Jobs: find them, open them, book them, move them along, and write on them. Every read starts
/// from the caller's <see cref="AccessScope"/>; every change goes through <see cref="JobRules"/>
/// and is announced through <see cref="IJobEvents"/>.
/// </summary>
public static class JobEndpoints
{
    /// <summary>The arrival windows customers can book, by starting hour (local time).</summary>
    public static readonly int[] WindowStarts = [8, 10, 12, 14];

    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder routes)
    {
        var jobs = routes.MapGroup("/jobs").WithTags("Jobs");

        jobs.MapGet("/", ListAsync).WithSummary("Jobs in scope, by day, status, technician and search.");
        jobs.MapGet("/{id:int}", GetAsync).WithSummary("One job, with what the caller may do next.");
        jobs.MapPost("/", BookAsync).WithSummary("Book a job at a customer's home.");
        jobs.MapPost("/{id:int}/status", ChangeStatusAsync).WithSummary("Move a job along: start, complete or cancel.");
        jobs.MapPost("/{id:int}/notes", AddNoteAsync).WithSummary("Write a note on a job.");

        return routes;
    }

    // ------------------------------------------------------------------ list

    internal static async Task<Ok<JobPage>> ListAsync(
        [AsParameters] JobListQuery query, AccessScope scope, TruckrollDbContext db, CompanyClock clock, CancellationToken cancellationToken)
    {
        var jobs = scope.Jobs(db);
        var (startOfToday, endOfToday) = DayBounds(clock.Today);

        jobs = query.Day switch
        {
            "today" => jobs.Where(j => j.WindowStart >= startOfToday && j.WindowStart < endOfToday),
            "upcoming" => jobs.Where(j => j.WindowStart >= endOfToday),
            "past" => jobs.Where(j => j.WindowStart < startOfToday),
            _ => jobs,
        };

        if (Enum.TryParse<JobStatus>(query.Status, out var status))
        {
            jobs = jobs.Where(j => j.Status == status);
        }
        else if (query.Status == "open")
        {
            jobs = jobs.Where(j => j.Status != JobStatus.Completed && j.Status != JobStatus.Cancelled);
        }

        if (query.Technician is { } technician)
        {
            jobs = technician == 0 ? jobs.Where(j => j.TechnicianId == null) : jobs.Where(j => j.TechnicianId == technician);
        }

        foreach (var word in (query.Q ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4))
        {
            jobs = jobs.Where(j => j.Number.Contains(word) || j.Summary.Contains(word) || j.Customer.LastName.Contains(word) || j.Property.Street.Contains(word));
        }

        var total = await jobs.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(query.PageSize ?? 25, 5, 100);
        var page = Math.Max(query.Page ?? 1, 1);

        // Past jobs newest first; today and upcoming in the order they'll happen.
        var ordered = query.Day is "past" or null or "" or "all"
            ? jobs.OrderByDescending(j => j.WindowStart).ThenBy(j => j.Number)
            : jobs.OrderBy(j => j.WindowStart).ThenBy(j => j.Priority).ThenBy(j => j.Number);

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new JobListItem(
                j.Id,
                j.Number,
                j.Type,
                j.Priority,
                j.Status,
                j.WindowStart,
                j.WindowEnd,
                j.Summary,
                j.CustomerId,
                j.Customer.FirstName + " " + j.Customer.LastName,
                j.Property.Street + ", " + j.Property.City,
                j.Branch.Code,
                j.Equipment == null ? null : j.Equipment.Kind,
                j.Technician == null ? null : j.Technician.FirstName + " " + j.Technician.LastName,
                j.Technician == null ? null : j.Technician.Color))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new JobPage(items, total, page, pageSize));
    }

    // ------------------------------------------------------------------ detail

    internal static async Task<Results<Ok<JobDetail>, NotFound>> GetAsync(int id, AccessScope scope, TruckrollDbContext db, CancellationToken cancellationToken)
    {
        var job = await LoadAsync(id, scope, db, cancellationToken);
        return job is null ? TypedResults.NotFound() : TypedResults.Ok(Describe(job, scope));
    }

    private static Task<Job?> LoadAsync(int id, AccessScope scope, TruckrollDbContext db, CancellationToken cancellationToken) =>
        scope.Jobs(db)
            .Include(j => j.Branch)
            .Include(j => j.Customer)
            .Include(j => j.Property)
            .Include(j => j.Equipment)
            .Include(j => j.Technician)
            .Include(j => j.Notes)
            .AsSplitQuery()
            .SingleOrDefaultAsync(j => j.Id == id, cancellationToken);

    private static JobDetail Describe(Job job, AccessScope scope) => new(
        job.Id,
        job.Number,
        job.Type,
        job.Priority,
        job.Status,
        job.WindowStart,
        job.WindowEnd,
        job.EstimatedMinutes,
        job.Summary,
        job.Resolution,
        job.InvoiceTotal,
        job.FirstTimeFix,
        job.RequiredSkill,
        job.RequiredCertification,
        job.CreatedAt,
        job.DispatchedAt,
        job.CompletedAt,
        new JobBranch(job.Branch.Code, job.Branch.Name),
        new JobCustomer(job.CustomerId, job.Customer.FirstName + " " + job.Customer.LastName, job.Customer.Phone),
        new JobProperty(job.Property.Street, job.Property.City, job.Property.AccessNotes, job.Property.Latitude, job.Property.Longitude),
        job.Equipment is { } e ? new JobEquipment(e.Id, e.Kind, e.Brand, e.Model, e.SerialNumber, e.InstalledOn, e.WarrantyExpiresOn) : null,
        job.Technician is { } t ? new JobTechnician(t.Id, t.FullName, t.Color) : null,
        job.Notes.OrderBy(n => n.CreatedAt).Select(n => new JobNoteItem(n.Id, n.Author, n.Source, n.Body, n.CreatedAt)).ToList(),
        JobRules.NextStatuses(job.Status).Where(s => JobRules.MayMove(scope, job, s)).ToList(),
        CanAddNotes: true);

    // ------------------------------------------------------------------ booking

    internal static async Task<Results<Created<JobDetail>, ValidationProblem, ForbidHttpResult>> BookAsync(
        BookJobRequest request, AccessScope scope, TruckrollDbContext db, MySqlDataSource numbers, CompanyClock clock, IJobEvents events, CancellationToken cancellationToken)
    {
        if (!scope.CanBookJobs)
        {
            return TypedResults.Forbid();
        }

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Summary) || request.Summary.Trim().Length < 5)
        {
            errors["summary"] = ["Describe the problem in a few words, the way the customer put it."];
        }

        if (!WindowStarts.Contains(request.WindowStartHour))
        {
            errors["windowStartHour"] = ["Pick one of the arrival windows: 8, 10, 12 or 2."];
        }

        if (request.Date < clock.Today)
        {
            errors["date"] = ["That day has passed. Pick today or later."];
        }

        // The property must belong to a customer the caller can see, and the equipment to that property.
        var property = await scope.Properties(db)
            .Include(p => p.Customer)
            .Include(p => p.Equipment)
            .SingleOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
        {
            errors["propertyId"] = ["Pick a home from this customer's list."];
        }

        var equipment = request.EquipmentId is { } equipmentId ? property?.Equipment.SingleOrDefault(e => e.Id == equipmentId) : null;
        if (request.EquipmentId is not null && property is not null && equipment is null)
        {
            errors["equipmentId"] = ["That equipment isn't at this home."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var (skill, certification) = JobRules.RequirementsFor(equipment?.Kind, request.Type);
        var start = AtLocal(request.Date, request.WindowStartHour);
        var job = new Job
        {
            Number = "",
            BranchId = property!.Customer.BranchId,
            CustomerId = property.CustomerId,
            PropertyId = property.Id,
            EquipmentId = equipment?.Id,
            Type = request.Type,
            Priority = request.Priority,
            Status = JobStatus.Scheduled,
            RequiredSkill = skill,
            RequiredCertification = certification,
            WindowStart = start,
            WindowEnd = start.AddHours(2),
            EstimatedMinutes = request.Type == JobType.Installation ? 360 : 90,
            Summary = request.Summary!.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Add(job);

        // The number comes from the database's counter, on a connection of its own, so bookings at
        // the same moment get different numbers on the first try (JobNumbers, #50). If this
        // booking rolls back, its number goes unused; numbers are unique, not gapless.
        job.Number = await JobNumbers.NextAsync(numbers, cancellationToken);

        // The job and its "booked" event commit together. Approving a call books inside its own,
        // bigger transaction; then this joins that one, and the call decides when to commit.
        await using (var work = await db.BeginOrJoinTransactionAsync(cancellationToken))
        {
            await db.SaveChangesAsync(cancellationToken);
            await events.PublishAsync(Changed(job, "booked", scope), cancellationToken);
            await work.CommitAsync(cancellationToken);
        }

        var saved = await LoadAsync(job.Id, scope, db, cancellationToken);
        return TypedResults.Created($"/api/jobs/{job.Id}", Describe(saved!, scope));
    }

    // ------------------------------------------------------------------ status

    internal static async Task<Results<Ok<JobDetail>, NotFound, ValidationProblem, ForbidHttpResult>> ChangeStatusAsync(
        int id, ChangeStatusRequest request, AccessScope scope, TruckrollDbContext db, IJobEvents events, CancellationToken cancellationToken)
    {
        var job = await LoadAsync(id, scope, db, cancellationToken);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (!JobRules.NextStatuses(job.Status).Contains(request.Status))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = [$"A {job.Status.ToString().ToLowerInvariant()} job can't move to {request.Status.ToString().ToLowerInvariant()}."],
            });
        }

        if (!JobRules.MayMove(scope, job, request.Status))
        {
            return TypedResults.Forbid();
        }

        if (request.Status == JobStatus.Completed)
        {
            if (string.IsNullOrWhiteSpace(request.Resolution))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["resolution"] = ["Say what you found and what you did. It's the first thing read on the next call."],
                });
            }

            job.Resolution = request.Resolution.Trim();
            job.InvoiceTotal = request.InvoiceTotal;
            job.FirstTimeFix = request.FirstTimeFix ?? true;
            job.CompletedAt = DateTimeOffset.UtcNow;
        }

        job.Status = request.Status;
        await using var work = await db.BeginOrJoinTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(Changed(job, request.Status.ToString().ToLowerInvariant(), scope), cancellationToken);
        await work.CommitAsync(cancellationToken);
        return TypedResults.Ok(Describe(job, scope));
    }

    // ------------------------------------------------------------------ notes

    internal static async Task<Results<Created<JobNoteItem>, NotFound, ValidationProblem>> AddNoteAsync(
        int id, AddNoteRequest request, AccessScope scope, TruckrollDbContext db, IJobEvents events, CancellationToken cancellationToken)
    {
        var job = await LoadAsync(id, scope, db, cancellationToken);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["Write something first."] });
        }

        var note = new JobNote
        {
            JobId = job.Id,
            Author = scope.DisplayName,
            Source = scope.Role == UserRole.Technician ? NoteSource.Technician : NoteSource.Office,
            Body = request.Body.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await using var work = await db.BeginOrJoinTransactionAsync(cancellationToken);
        db.Add(note);
        await db.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(Changed(job, "noted", scope), cancellationToken);
        await work.CommitAsync(cancellationToken);

        return TypedResults.Created($"/api/jobs/{job.Id}", new JobNoteItem(note.Id, note.Author, note.Source, note.Body, note.CreatedAt));
    }

    // ------------------------------------------------------------------ helpers

    private static JobChanged Changed(Job job, string change, AccessScope scope) =>
        new(job.Id, job.Number, job.BranchId, job.Status, job.TechnicianId, change, scope.DisplayName, DateTimeOffset.UtcNow);

    private static DateTimeOffset AtLocal(DateOnly day, int hour)
    {
        var local = day.ToDateTime(new TimeOnly(hour, 0));
        return new DateTimeOffset(local, CompanyClock.Zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>A company-local day as a UTC range, for filtering stored times.</summary>
    internal static (DateTimeOffset Start, DateTimeOffset End) DayBounds(DateOnly day) => (AtLocal(day, 0), AtLocal(day.AddDays(1), 0));
}

public sealed record JobListQuery(string? Day, string? Status, int? Technician, string? Q, int? Page, int? PageSize);

public sealed record JobPage(List<JobListItem> Items, int Total, int Page, int PageSize);

public sealed record JobListItem(
    int Id,
    string Number,
    JobType Type,
    JobPriority Priority,
    JobStatus Status,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    string Summary,
    int CustomerId,
    string Customer,
    string Address,
    string BranchCode,
    EquipmentKind? EquipmentKind,
    string? Technician,
    string? TechnicianColor);

public sealed record JobDetail(
    int Id,
    string Number,
    JobType Type,
    JobPriority Priority,
    JobStatus Status,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    int EstimatedMinutes,
    string Summary,
    string? Resolution,
    decimal? InvoiceTotal,
    bool? FirstTimeFix,
    TechnicianSkills RequiredSkill,
    Certifications RequiredCertification,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? CompletedAt,
    JobBranch Branch,
    JobCustomer Customer,
    JobProperty Property,
    JobEquipment? Equipment,
    JobTechnician? Technician,
    List<JobNoteItem> Notes,
    List<JobStatus> NextStatuses,
    bool CanAddNotes);

public sealed record JobBranch(string Code, string Name);
public sealed record JobCustomer(int Id, string Name, string Phone);
public sealed record JobProperty(string Street, string City, string? AccessNotes, double Latitude, double Longitude);
public sealed record JobEquipment(int Id, EquipmentKind Kind, string Brand, string Model, string SerialNumber, DateOnly InstalledOn, DateOnly? WarrantyExpiresOn);
public sealed record JobTechnician(int Id, string Name, string Color);
public sealed record JobNoteItem(int Id, string Author, NoteSource Source, string Body, DateTimeOffset CreatedAt);

/// <summary>What the booking form sends. Skill and licence are deliberately absent: the server decides.</summary>
public sealed record BookJobRequest(int PropertyId, int? EquipmentId, JobType Type, JobPriority Priority, DateOnly Date, int WindowStartHour, string? Summary);

public sealed record ChangeStatusRequest(JobStatus Status, string? Resolution, decimal? InvoiceTotal, bool? FirstTimeFix);

public sealed record AddNoteRequest(string? Body);
