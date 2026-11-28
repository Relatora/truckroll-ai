using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Truckroll.Api.Access;
using Truckroll.Api.Common;
using Truckroll.Api.Features.Dispatch;
using Truckroll.Api.Features.Jobs;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Calls;

/// <summary>
/// The calls inbox: a transcript in, a proposed job and follow-up out, and nothing created until
/// a person approves it. Approved follow-ups wait in an outbox; the app never sends email.
/// </summary>
public static class CallEndpoints
{
    public const int MinTranscriptLength = 40;
    public const int MaxTranscriptLength = 20_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapCallEndpoints(this IEndpointRouteBuilder routes)
    {
        var calls = routes.MapGroup("/calls").WithTags("Calls");
        calls.MapGet("/", ListAsync).WithSummary("The inbox, newest first; filter with ?status=.");
        calls.MapPost("/", PasteAsync).WithSummary("Add a call by pasting its transcript.");
        calls.MapGet("/{id:int}", GetAsync).WithSummary("One call: transcript, proposal and draft follow-up.");
        calls.MapPost("/{id:int}/analyse", AnalyseAsync).WithSummary("Have the model propose a job and a follow-up. Creates nothing.");
        calls.MapPost("/{id:int}/approve", ApproveAsync).WithSummary("Book the reviewed job and file the follow-up in the outbox.");
        calls.MapPost("/{id:int}/dismiss", DismissAsync).WithSummary("Close the call without creating anything.");
        return routes;
    }

    // ------------------------------------------------------------------ reading

    internal static async Task<Results<Ok<List<CallListItem>>, ForbidHttpResult>> ListAsync(
        CallStatus? status, AccessScope scope, TruckrollDbContext db, CancellationToken cancellationToken)
    {
        if (!scope.CanReviewCalls)
        {
            return TypedResults.Forbid();
        }

        var calls = await scope.Calls(db).AsNoTracking()
            .Where(c => status == null || c.Status == status)
            .OrderByDescending(c => c.ReceivedAt)
            .Select(c => new { c.Id, c.Branch.Code, c.ReceivedAt, c.Status, c.Summary, c.ProposedJobJson, c.Transcript })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(calls.Select(c =>
        {
            var proposal = Read(c.ProposedJobJson);
            return new CallListItem(c.Id, c.Code, c.ReceivedAt, c.Status, proposal?.CallerName, proposal?.Priority, c.Summary ?? FirstCallerLine(c.Transcript));
        }).ToList());
    }

    internal static async Task<Results<Ok<CallDetail>, NotFound, ForbidHttpResult>> GetAsync(
        int id, AccessScope scope, TruckrollDbContext db, CancellationToken cancellationToken)
    {
        if (!scope.CanReviewCalls)
        {
            return TypedResults.Forbid();
        }

        var call = await LoadAsync(id, scope, db, cancellationToken);
        return call is null ? TypedResults.NotFound() : TypedResults.Ok(Describe(call));
    }

    internal static async Task<Results<Created<CallDetail>, ValidationProblem, ForbidHttpResult>> PasteAsync(
        PasteCallRequest request, AccessScope scope, TruckrollDbContext db, CancellationToken cancellationToken)
    {
        if (!scope.CanReviewCalls)
        {
            return TypedResults.Forbid();
        }

        var transcript = request.Transcript?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (transcript.Length is < MinTranscriptLength or > MaxTranscriptLength)
        {
            errors["transcript"] = [$"Paste the whole conversation ({MinTranscriptLength} to {MaxTranscriptLength} characters)."];
        }

        // Office staff paste into their own branch; the owner says which.
        var branch = await scope.Branches(db)
            .Where(b => request.BranchCode == null ? b.Id == scope.BranchId : b.Code == request.BranchCode)
            .FirstOrDefaultAsync(cancellationToken);
        if (branch is null)
        {
            errors["branchCode"] = ["Pick the branch that took the call."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var call = new CallRecord { BranchId = branch!.Id, ReceivedAt = DateTimeOffset.UtcNow, Transcript = transcript, Status = CallStatus.New };
        db.Add(call);
        await db.SaveChangesAsync(cancellationToken);

        var saved = await LoadAsync(call.Id, scope, db, cancellationToken);
        return TypedResults.Created($"/api/calls/{call.Id}", Describe(saved!));
    }

    // ------------------------------------------------------------------ the model's part

    internal static async Task<Results<Ok<CallDetail>, NotFound, Conflict<ProblemMessage>, UnprocessableEntity<AnalysisFailed>, ForbidHttpResult>> AnalyseAsync(
        int id, AccessScope scope, TruckrollDbContext db, CallAnalyser analyser, CancellationToken cancellationToken)
    {
        if (!scope.CanReviewCalls)
        {
            return TypedResults.Forbid();
        }

        var call = await scope.Calls(db).Include(c => c.Branch).SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (call is null)
        {
            return TypedResults.NotFound();
        }

        if (call.Status is CallStatus.Approved or CallStatus.Dismissed)
        {
            return TypedResults.Conflict(new ProblemMessage($"This call was {call.Status.ToString().ToLowerInvariant()} by {call.ReviewedBy}; it's closed."));
        }

        var outcome = await analyser.AnalyseAsync(call, scope, cancellationToken);
        if (outcome.Proposal is null)
        {
            return TypedResults.UnprocessableEntity(new AnalysisFailed("The model's answer couldn't be used. Try again, or book the job by hand.", outcome.Problems));
        }

        // A proposal, not a record: the call holds it until a person decides.
        call.Summary = outcome.Summary;
        call.ProposedJobJson = JsonSerializer.Serialize(outcome.Proposal, Json);
        call.DraftEmailSubject = outcome.FollowUpSubject;
        call.DraftEmailBody = outcome.FollowUpBody;
        call.Status = CallStatus.AwaitingReview;
        await db.SaveChangesAsync(cancellationToken);

        var saved = await LoadAsync(call.Id, scope, db, cancellationToken);
        return TypedResults.Ok(Describe(saved!));
    }

    // ------------------------------------------------------------------ the person's part

    internal static async Task<Results<Ok<CallDetail>, NotFound, ValidationProblem, Conflict<ProblemMessage>, ForbidHttpResult>> ApproveAsync(
        int id, ApproveCallRequest request, AccessScope scope, TruckrollDbContext db, MySqlDataSource numbers, CompanyClock clock, IJobEvents events, CancellationToken cancellationToken)
    {
        if (!scope.CanReviewCalls || !scope.CanBookJobs)
        {
            return TypedResults.Forbid();
        }

        var call = await scope.Calls(db).Include(c => c.Branch).SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (call is null)
        {
            return TypedResults.NotFound();
        }

        if (call.Status != CallStatus.AwaitingReview)
        {
            return TypedResults.Conflict(new ProblemMessage(call.Status == CallStatus.New
                ? "Analyse the call first, then review the proposal."
                : $"This call was already {call.Status.ToString().ToLowerInvariant()} by {call.ReviewedBy}."));
        }

        var errors = new Dictionary<string, string[]>();
        if ((request.PropertyId is null) == (request.NewCustomer is null))
        {
            errors["customer"] = ["Pick the caller's home, or add them as a new customer."];
        }

        if (string.IsNullOrWhiteSpace(request.FollowUpSubject) || string.IsNullOrWhiteSpace(request.FollowUpBody))
        {
            errors["followUp"] = ["The follow-up needs a subject and a message."];
        }

        if (request.NewCustomer is { } fresh && (string.IsNullOrWhiteSpace(fresh.FirstName) || string.IsNullOrWhiteSpace(fresh.LastName) || string.IsNullOrWhiteSpace(fresh.Street)))
        {
            errors["newCustomer"] = ["A new customer needs a first name, last name and street address."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // One transaction: a new customer, their home and the job either all exist or none do.
        // READ COMMITTED, not MySQL's default REPEATABLE READ: booking picks the next job number
        // as MAX + 1 and retries on a clash, and under a frozen snapshot every retry would read
        // the same stale maximum. (Found by the tests: parallel bookings made this one fail.)
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var propertyId = request.PropertyId;
        if (request.NewCustomer is { } add)
        {
            var customer = new Customer
            {
                BranchId = call.BranchId,
                FirstName = add.FirstName.Trim(),
                LastName = add.LastName.Trim(),
                Email = add.Email?.Trim() ?? "",
                Phone = add.Phone?.Trim() ?? "",
                Source = add.Source ?? CustomerSource.Referral,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            // No geocoder in v0.1: the home is placed at the branch until someone corrects it, so
            // dispatch distances to it are rough.
            customer.Properties.Add(new Property
            {
                Street = add.Street.Trim(),
                City = call.Branch.City,
                PostalCode = "",
                Latitude = call.Branch.Latitude,
                Longitude = call.Branch.Longitude,
                Kind = PropertyKind.Detached,
            });
            db.Add(customer);
            await db.SaveChangesAsync(cancellationToken);
            propertyId = customer.Properties[0].Id;
        }

        // The job is booked exactly as the booking form books one: same validation, numbering,
        // licence rules and broadcast to the dispatch board.
        var booked = await JobEndpoints.BookAsync(
            new BookJobRequest(propertyId!.Value, request.EquipmentId, request.Type, request.Priority, request.Date, request.WindowStartHour, request.Summary),
            scope, db, numbers, clock, events, cancellationToken);
        if (booked.Result is not Created<JobDetail> created)
        {
            await transaction.RollbackAsync(cancellationToken);
            return booked.Result is ValidationProblem problem ? problem : TypedResults.Forbid();
        }

        var job = created.Value!;
        call.CustomerId = job.Customer.Id;
        call.CreatedJobId = job.Id;
        call.DraftEmailSubject = request.FollowUpSubject!.Trim();
        call.DraftEmailBody = request.FollowUpBody!.Trim();
        call.Status = CallStatus.Approved;
        call.ReviewedBy = scope.DisplayName;
        call.ReviewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var saved = await LoadAsync(call.Id, scope, db, cancellationToken);
        return TypedResults.Ok(Describe(saved!));
    }

    internal static async Task<Results<Ok<CallDetail>, NotFound, Conflict<ProblemMessage>, ForbidHttpResult>> DismissAsync(
        int id, AccessScope scope, TruckrollDbContext db, CancellationToken cancellationToken)
    {
        if (!scope.CanReviewCalls)
        {
            return TypedResults.Forbid();
        }

        var call = await scope.Calls(db).SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (call is null)
        {
            return TypedResults.NotFound();
        }

        if (call.Status is CallStatus.Approved or CallStatus.Dismissed)
        {
            return TypedResults.Conflict(new ProblemMessage($"This call was already {call.Status.ToString().ToLowerInvariant()}."));
        }

        call.Status = CallStatus.Dismissed;
        call.ReviewedBy = scope.DisplayName;
        call.ReviewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var saved = await LoadAsync(call.Id, scope, db, cancellationToken);
        return TypedResults.Ok(Describe(saved!));
    }

    // ------------------------------------------------------------------ shapes

    private static Task<CallRecord?> LoadAsync(int id, AccessScope scope, TruckrollDbContext db, CancellationToken cancellationToken) =>
        scope.Calls(db).AsNoTracking().Include(c => c.Branch).Include(c => c.CreatedJob).SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    private static CallDetail Describe(CallRecord c) => new(
        c.Id,
        new CallBranch(c.Branch.Code, c.Branch.Name, c.Branch.Phone),
        c.ReceivedAt,
        c.Status,
        c.Transcript,
        c.Summary,
        Read(c.ProposedJobJson),
        c.DraftEmailSubject,
        c.DraftEmailBody,
        c.CreatedJob is null ? null : new CallJob(c.CreatedJob.Id, c.CreatedJob.Number),
        c.ReviewedBy,
        c.ReviewedAt);

    private static CallProposal? Read(string? json) => json is null ? null : JsonSerializer.Deserialize<CallProposal>(json, Json);

    /// <summary>Before analysis, the list shows the caller's first words.</summary>
    private static string FirstCallerLine(string transcript)
    {
        var line = transcript.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Caller:", StringComparison.OrdinalIgnoreCase)) ?? transcript;
        line = line.StartsWith("Caller:", StringComparison.OrdinalIgnoreCase) ? line[7..].Trim() : line;
        return line.Length > 110 ? line[..110].TrimEnd() + "…" : line;
    }
}

public sealed record CallListItem(int Id, string BranchCode, DateTimeOffset ReceivedAt, CallStatus Status, string? Caller, JobPriority? Priority, string Preview);

public sealed record CallBranch(string Code, string Name, string Phone);

public sealed record CallJob(int Id, string Number);

public sealed record CallDetail(
    int Id,
    CallBranch Branch,
    DateTimeOffset ReceivedAt,
    CallStatus Status,
    string Transcript,
    string? Summary,
    CallProposal? Proposal,
    string? FollowUpSubject,
    string? FollowUpBody,
    CallJob? CreatedJob,
    string? ReviewedBy,
    DateTimeOffset? ReviewedAt);

public sealed record PasteCallRequest(string? BranchCode, string? Transcript);

/// <summary>The reviewed proposal: an existing home, or a new customer, plus the job and the follow-up.</summary>
public sealed record ApproveCallRequest(
    int? PropertyId,
    NewCustomerRequest? NewCustomer,
    int? EquipmentId,
    JobType Type,
    JobPriority Priority,
    DateOnly Date,
    int WindowStartHour,
    string? Summary,
    string? FollowUpSubject,
    string? FollowUpBody);

public sealed record NewCustomerRequest(string FirstName, string LastName, string Street, string? Email, string? Phone, CustomerSource? Source);

public sealed record AnalysisFailed(string Detail, List<string> Problems);
