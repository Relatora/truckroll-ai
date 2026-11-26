using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Truckroll.Api.Access;
using Truckroll.Api.Common;
using Truckroll.Api.Features.Jobs;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Dispatch;

/// <summary>
/// The dispatch board: one branch's day, and assigning work to the people who can legally do it.
/// </summary>
public static class DispatchEndpoints
{
    public static IEndpointRouteBuilder MapDispatchEndpoints(this IEndpointRouteBuilder routes)
    {
        var dispatch = routes.MapGroup("/dispatch").WithTags("Dispatch");

        dispatch.MapGet("/board", GetBoardAsync).WithSummary("A branch's technicians and jobs for one day.");
        dispatch.MapPost("/assign", AssignAsync).WithSummary("Give a job to a technician who holds the licence and is free.");
        dispatch.MapPost("/unassign", UnassignAsync).WithSummary("Take a job back off a technician's day.");

        return routes;
    }

    // ------------------------------------------------------------------ the board

    internal static async Task<Results<Ok<Board>, NotFound>> GetBoardAsync(
        DateOnly? date, string? branch, AccessScope scope, TruckrollDbContext db, CompanyClock clock, CancellationToken cancellationToken)
    {
        var day = date ?? clock.Today;
        var branches = scope.Branches(db);
        var chosen = await (string.IsNullOrEmpty(branch)
                ? branches.OrderBy(b => b.Id == scope.BranchId ? 0 : 1).ThenBy(b => b.Name)
                : branches.Where(b => b.Code == branch))
            .FirstOrDefaultAsync(cancellationToken);
        if (chosen is null)
        {
            return TypedResults.NotFound();
        }

        var (start, end) = JobEndpoints.DayBounds(day);
        var jobs = await scope.Jobs(db)
            .Where(j => j.BranchId == chosen.Id && j.WindowStart >= start && j.WindowStart < end && j.Status != JobStatus.Cancelled)
            .OrderBy(j => j.WindowStart)
            .Select(j => new BoardJob(
                j.Id,
                j.Number,
                j.Type,
                j.Priority,
                j.Status,
                j.WindowStart,
                j.WindowEnd,
                j.EstimatedMinutes,
                j.Summary,
                j.Customer.FirstName + " " + j.Customer.LastName,
                j.Property.Street,
                j.Property.Latitude,
                j.Property.Longitude,
                j.Equipment == null ? null : j.Equipment.Kind,
                j.RequiredCertification,
                j.TechnicianId))
            .ToListAsync(cancellationToken);

        // The branch's own technicians, plus anyone from another branch covering a job here today.
        var local = await scope.Technicians(db)
            .Where(t => t.IsActive && t.BranchId == chosen.Id)
            .OrderBy(t => t.FirstName)
            .Select(t => new BoardTechnician(t.Id, t.FirstName + " " + t.LastName, t.Color, t.Skills, t.Certifications, false))
            .ToListAsync(cancellationToken);
        var visiting = await scope.Jobs(db)
            .Where(j => j.BranchId == chosen.Id && j.WindowStart >= start && j.WindowStart < end && j.Technician != null && j.Technician.BranchId != chosen.Id)
            .Select(j => new BoardTechnician(j.Technician!.Id, j.Technician.FirstName + " " + j.Technician.LastName, j.Technician.Color, j.Technician.Skills, j.Technician.Certifications, true))
            .Distinct()
            .ToListAsync(cancellationToken);
        var technicians = local.Concat(visiting.Where(v => local.All(l => l.Id != v.Id))).ToList();

        return TypedResults.Ok(new Board(
            day,
            new BoardBranch(chosen.Id, chosen.Code, chosen.Name, chosen.Latitude, chosen.Longitude),
            scope.CanDispatch,
            technicians,
            jobs));
    }

    // ------------------------------------------------------------------ assigning

    internal static async Task<Results<Ok<BoardJob>, NotFound, ValidationProblem, Conflict<ProblemMessage>, ForbidHttpResult>> AssignAsync(
        AssignRequest request, AccessScope scope, TruckrollDbContext db, IJobEvents events, CancellationToken cancellationToken)
    {
        if (!scope.CanDispatch)
        {
            return TypedResults.Forbid();
        }

        var job = await scope.Jobs(db).Include(j => j.Customer).Include(j => j.Property).Include(j => j.Equipment)
            .SingleOrDefaultAsync(j => j.Id == request.JobId, cancellationToken);
        // A branch dispatcher assigns their own branch's technicians; the owner can send anyone.
        var technician = await scope.Technicians(db).SingleOrDefaultAsync(t => t.Id == request.TechnicianId && t.IsActive, cancellationToken);
        if (job is null || technician is null)
        {
            return TypedResults.NotFound();
        }

        if (job.Status is not (JobStatus.Scheduled or JobStatus.Dispatched))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["jobId"] = [$"{job.Number} is {job.Status.ToString().ToLowerInvariant()}; only waiting or assigned jobs can be moved."],
            });
        }

        // The hard rule: no licence, no job. Proximity and skill are preferences; this isn't.
        if ((technician.Certifications & job.RequiredCertification) != job.RequiredCertification)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["technicianId"] = [$"{technician.FullName} doesn't hold the {Describe(job.RequiredCertification)} this job needs."],
            });
        }

        var clash = await Schedules.FindClashAsync(db, technician.Id, job.WindowStart, job.WindowEnd, job.Id, cancellationToken);
        if (clash is not null)
        {
            return TypedResults.Conflict(new ProblemMessage($"{technician.FullName} already has {clash} in that window."));
        }

        job.TechnicianId = technician.Id;
        job.Status = JobStatus.Dispatched;
        job.DispatchedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await events.PublishAsync(new JobChanged(job.Id, job.Number, job.BranchId, job.Status, job.TechnicianId, $"assigned to {technician.FullName}", scope.DisplayName, DateTimeOffset.UtcNow), cancellationToken);
        return TypedResults.Ok(ToBoardJob(job));
    }

    internal static async Task<Results<Ok<BoardJob>, NotFound, ValidationProblem, ForbidHttpResult>> UnassignAsync(
        UnassignRequest request, AccessScope scope, TruckrollDbContext db, IJobEvents events, CancellationToken cancellationToken)
    {
        if (!scope.CanDispatch)
        {
            return TypedResults.Forbid();
        }

        var job = await scope.Jobs(db).Include(j => j.Customer).Include(j => j.Property).Include(j => j.Equipment)
            .SingleOrDefaultAsync(j => j.Id == request.JobId, cancellationToken);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (job.Status != JobStatus.Dispatched)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["jobId"] = ["Only an assigned job that hasn't started can go back to the waiting lane."],
            });
        }

        job.TechnicianId = null;
        job.Status = JobStatus.Scheduled;
        job.DispatchedAt = null;
        await db.SaveChangesAsync(cancellationToken);

        await events.PublishAsync(new JobChanged(job.Id, job.Number, job.BranchId, job.Status, null, "unassigned", scope.DisplayName, DateTimeOffset.UtcNow), cancellationToken);
        return TypedResults.Ok(ToBoardJob(job));
    }

    private static BoardJob ToBoardJob(Job j) => new(
        j.Id, j.Number, j.Type, j.Priority, j.Status, j.WindowStart, j.WindowEnd, j.EstimatedMinutes, j.Summary,
        j.Customer.FullName, j.Property.Street, j.Property.Latitude, j.Property.Longitude, j.Equipment?.Kind, j.RequiredCertification, j.TechnicianId);

    private static string Describe(Certifications certification) => certification switch
    {
        Certifications.GasG2 => "G2 gas licence",
        Certifications.GasG1 => "G1 gas licence",
        Certifications.Refrigeration313A => "313A refrigeration licence",
        Certifications.Plumber306A => "306A plumber's licence",
        _ => "licence",
    };
}

public sealed record Board(DateOnly Date, BoardBranch Branch, bool CanDispatch, List<BoardTechnician> Technicians, List<BoardJob> Jobs);
public sealed record BoardBranch(int Id, string Code, string Name, double Latitude, double Longitude);
public sealed record BoardTechnician(int Id, string Name, string Color, TechnicianSkills Skills, Certifications Certifications, bool Visiting);

public sealed record BoardJob(
    int Id,
    string Number,
    JobType Type,
    JobPriority Priority,
    JobStatus Status,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    int EstimatedMinutes,
    string Summary,
    string Customer,
    string Street,
    double Latitude,
    double Longitude,
    EquipmentKind? EquipmentKind,
    Certifications RequiredCertification,
    int? TechnicianId);

public sealed record AssignRequest(int JobId, int TechnicianId);
public sealed record UnassignRequest(int JobId);
public sealed record ProblemMessage(string Detail);
