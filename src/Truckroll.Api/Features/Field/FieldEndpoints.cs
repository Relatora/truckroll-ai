using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Truckroll.Api.Access;
using Truckroll.Api.Common;
using Truckroll.Api.Data;
using Truckroll.Api.Features.Jobs;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Field;

/// <summary>
/// The technician app's API: sign in once, take the day's work, and send back what was done,
/// possibly hours later, after a basement with no signal (#22, docs/adr/011).
/// </summary>
/// <remarks>
/// Built for a phone that's offline more than a browser ever is. The day comes down in one
/// response with everything needed to work without a connection. Changes go up in one batch, each
/// with an id the phone made, so a batch sent twice (the phone never heard back the first time)
/// is answered twice and applied once.
/// </remarks>
public static class FieldEndpoints
{
    /// <summary>The policy on everything but sign-in: a personal token, held by a technician.</summary>
    public const string Policy = AccessServiceExtensions.FieldAppPolicy;

    /// <summary>How much service history comes down with each job.</summary>
    internal const int HistoryPerJob = 5;

    /// <summary>The most changes one sync takes. A day's work is a few dozen; this stops abuse, not people.</summary>
    internal const int MaxChangesPerSync = 200;

    public static IEndpointRouteBuilder MapFieldEndpoints(this IEndpointRouteBuilder routes)
    {
        var field = routes.MapGroup("/field").WithTags("Field app");

        field.MapPost("/sign-in", SignInAsync).AllowAnonymous()
            .WithSummary("A technician signs the app in and gets a device token, shown once.");

        field.MapGet("/day", DayAsync).RequireAuthorization(Policy)
            .WithSummary("The technician's jobs for a day, with everything needed to work offline.");

        field.MapPost("/sync", SyncAsync).RequireAuthorization(Policy)
            .WithSummary("Changes made in the app, in the order they were made. Safe to send twice.");

        return routes;
    }

    // ------------------------------------------------------------------ sign-in

    internal static async Task<Results<Ok<FieldSignInResponse>, UnauthorizedHttpResult, ProblemHttpResult, ValidationProblem>> SignInAsync(
        FieldSignInRequest request, SignInManager<AppUser> signIn, UserManager<AppUser> users, PersonalTokens tokens, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Enter your email and password."] });
        }

        var user = await users.FindByEmailAsync(request.Email.Trim());
        // Checks the password without setting a cookie, and counts failures toward lockout like
        // the web sign-in. Unknown email and wrong password get the same answer.
        if (user is null || !(await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded)
        {
            return TypedResults.Unauthorized();
        }

        // Only now, with the password proven, say what the account is: no use to a guesser.
        if (user.Role != UserRole.Technician || user.TechnicianId is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "The field app is for technicians",
                detail: "Sign in on the web instead. The office runs the board from there.");
        }

        var device = string.IsNullOrWhiteSpace(request.Device) ? "Phone" : request.Device.Trim();
        // The same tokens MCP uses: listed on the token page, where a lost phone can be revoked.
        var created = await tokens.CreateAsync(user.Id, $"Field app · {device[..Math.Min(device.Length, 40)]}", cancellationToken);
        if (created is not { } issued)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Too many signed-in devices",
                detail: $"You have {PersonalTokens.MaxLiveTokens} live tokens. Revoke one on the web, under MCP access, then try again.");
        }

        return TypedResults.Ok(new FieldSignInResponse(issued.Token, issued.Record.ExpiresAt, user.DisplayName, user.TechnicianId.Value));
    }

    // ------------------------------------------------------------------ the day

    internal static async Task<Ok<FieldDay>> DayAsync(
        DateOnly? date, AccessScope scope, TruckrollDbContext db, CompanyClock clock, TimeProvider time, CancellationToken cancellationToken)
    {
        var day = date ?? clock.Today;
        var (start, end) = JobEndpoints.DayBounds(day);

        // scope.Jobs is already "my jobs" for a technician.
        var jobs = await scope.Jobs(db)
            .Where(j => j.WindowStart >= start && j.WindowStart < end && j.Status != JobStatus.Cancelled)
            .Include(j => j.Customer)
            .Include(j => j.Property)
            .Include(j => j.Equipment)
            .Include(j => j.Notes)
            .OrderBy(j => j.WindowStart)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        // One query for every address's history, rather than one per job.
        var properties = jobs.Select(j => j.PropertyId).Distinct().ToList();
        var ids = jobs.Select(j => j.Id).ToList();
        var history = await scope.ServiceHistory(db)
            .Where(h => properties.Contains(h.PropertyId) && !ids.Contains(h.Id) && h.Status == JobStatus.Completed)
            .OrderByDescending(h => h.CompletedAt)
            .Select(h => new { h.PropertyId, Visit = new FieldVisit(h.Number, h.CompletedAt ?? h.WindowStart, h.Type, h.Summary, h.Resolution, h.Technician == null ? null : h.Technician.FirstName + " " + h.Technician.LastName) })
            .ToListAsync(cancellationToken);
        var historyAt = history.GroupBy(h => h.PropertyId).ToDictionary(g => g.Key, g => g.Select(h => h.Visit).Take(HistoryPerJob).ToList());

        return TypedResults.Ok(new FieldDay(
            day,
            time.GetUtcNow(),
            scope.DisplayName,
            [.. jobs.Select(job => Describe(job, historyAt.GetValueOrDefault(job.PropertyId) ?? []))]));
    }

    private static FieldJob Describe(Job job, List<FieldVisit> history) => new(
        job.Id,
        job.Number,
        job.Status,
        job.Type,
        job.Priority,
        job.WindowStart,
        job.WindowEnd,
        job.EstimatedMinutes,
        job.Summary,
        job.Resolution,
        new FieldCustomer(job.Customer.FullName, job.Customer.Phone),
        new FieldPlace(job.Property.Street, job.Property.City, job.Property.AccessNotes, job.Property.Latitude, job.Property.Longitude),
        job.Equipment is { } e ? new FieldEquipment(e.Kind.ToString(), e.Brand, e.Model, e.SerialNumber, e.InstalledOn, e.WarrantyExpiresOn) : null,
        history,
        [.. job.Notes.OrderBy(n => n.CreatedAt).Select(n => new FieldNote(n.Author, n.Source, n.Body, n.CreatedAt))]);

    // ------------------------------------------------------------------ sync

    internal static async Task<Results<Ok<FieldSyncResponse>, ValidationProblem>> SyncAsync(
        FieldSyncRequest request, AccessScope scope, TruckrollDbContext db, IJobEvents events, TimeProvider time, CancellationToken cancellationToken)
    {
        if (request.Changes is not { Count: > 0 and <= MaxChangesPerSync })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["changes"] = [$"Send between 1 and {MaxChangesPerSync} changes."],
            });
        }

        // In the order they were made: a start must land before the completion that follows it.
        var results = new List<FieldChangeResult>(request.Changes.Count);
        foreach (var change in request.Changes)
        {
            results.Add(await ApplyAsync(change, scope, db, events, time, cancellationToken));
            db.ChangeTracker.Clear(); // Each change stands alone: nothing tracked leaks into the next.
        }

        return TypedResults.Ok(new FieldSyncResponse(time.GetUtcNow(), results));
    }

    private static async Task<FieldChangeResult> ApplyAsync(
        FieldChangeRequest change, AccessScope scope, TruckrollDbContext db, IJobEvents events, TimeProvider time, CancellationToken cancellationToken)
    {
        // Seen it before? Then answer as before. This is what makes sending twice safe.
        var earlier = await db.FieldChanges.AsNoTracking().SingleOrDefaultAsync(c => c.Id == change.Id, cancellationToken);
        if (earlier is not null)
        {
            return earlier.UserId == scope.UserId
                ? new(change.Id, earlier.Outcome.ToString(), earlier.Explanation)
                : Invalid(change.Id, "That change id belongs to someone else.");
        }

        if (change.Id == Guid.Empty || !Enum.TryParse<FieldChangeKind>(change.Kind, ignoreCase: true, out var kind))
        {
            return Invalid(change.Id, "The app sent a change it doesn't understand.");
        }

        var text = (kind == FieldChangeKind.Note ? change.Body : change.Resolution)?.Trim();
        if (kind != FieldChangeKind.Start && string.IsNullOrEmpty(text))
        {
            return Invalid(change.Id, kind == FieldChangeKind.Note ? "The note was empty." : "A completion needs a resolution.");
        }

        // The job may no longer be theirs, which is exactly when the explanation matters.
        var job = await scope.JobsToExplain(db).Include(j => j.Technician).SingleOrDefaultAsync(j => j.Id == change.JobId, cancellationToken);
        if (job is null)
        {
            return Invalid(change.Id, "That job isn't in your branch any more.");
        }

        var now = time.GetUtcNow();
        // The phone's clock, but never in the future: a clock that's wrong mustn't complete a job tomorrow.
        var madeAt = change.MadeAt > now || change.MadeAt == default ? now : change.MadeAt;
        var decision = FieldRules.Decide(kind, job, scope.TechnicianId ?? 0);

        await using var work = await db.BeginOrJoinTransactionAsync(cancellationToken);
        string? happened = null;
        if (decision.NewStatus is { } status)
        {
            job.Status = status;
            if (status == JobStatus.Completed)
            {
                job.Resolution = text;
                job.FirstTimeFix = change.FirstTimeFix ?? true;
                job.CompletedAt = madeAt;
            }

            happened = status.ToString().ToLowerInvariant(); // inprogress → job.started, completed → job.completed
        }

        if (kind == FieldChangeKind.Note || decision.KeepTextAsNote)
        {
            db.Add(new JobNote
            {
                JobId = job.Id,
                Author = scope.DisplayName,
                Source = NoteSource.Technician,
                Body = kind == FieldChangeKind.Note ? text! : $"Resolution written in the field at {TimeZoneInfo.ConvertTime(madeAt, CompanyClock.Zone):h:mm tt}, not applied: {text}",
                CreatedAt = madeAt,
            });
            happened ??= "noted";
        }

        db.FieldChanges.Add(new FieldChange
        {
            Id = change.Id,
            UserId = scope.UserId!,
            JobId = job.Id,
            Kind = kind.ToString().ToLowerInvariant(),
            Outcome = decision.Outcome,
            Explanation = decision.Explanation,
            MadeAt = madeAt,
            ReceivedAt = now,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (work.Owned)
        {
            // Two syncs from one phone raced with the same change, and the other one won the
            // insert. Undo ours and give the winner's answer.
            await work.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var winner = await db.FieldChanges.AsNoTracking().SingleOrDefaultAsync(c => c.Id == change.Id, cancellationToken);
            if (winner is null || winner.UserId != scope.UserId)
            {
                throw;
            }

            return new(change.Id, winner.Outcome.ToString(), winner.Explanation);
        }

        if (happened is not null)
        {
            // The board, the search index and the notifications drafter hear about it like any
            // other change, through the outbox, in this same transaction.
            await events.PublishAsync(new JobChanged(job.Id, job.Number, job.BranchId, job.Status, job.TechnicianId, happened, scope.DisplayName, now), cancellationToken);
        }

        await work.CommitAsync(cancellationToken);
        return new(change.Id, decision.Outcome.ToString(), decision.Explanation);
    }

    /// <summary>Not recorded: an invalid change gets the same answer every time anyway.</summary>
    private static FieldChangeResult Invalid(Guid id, string why) => new(id, "Invalid", why);
}

public sealed record FieldSignInRequest(string Email, string Password, string? Device);

public sealed record FieldSignInResponse(string Token, DateTimeOffset ExpiresAt, string DisplayName, int TechnicianId);

public sealed record FieldDay(DateOnly Date, DateTimeOffset ServerTime, string Technician, List<FieldJob> Jobs);

public sealed record FieldJob(
    int Id,
    string Number,
    JobStatus Status,
    JobType Type,
    JobPriority Priority,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    int EstimatedMinutes,
    string Summary,
    string? Resolution,
    FieldCustomer Customer,
    FieldPlace Place,
    FieldEquipment? Equipment,
    List<FieldVisit> History,
    List<FieldNote> Notes);

public sealed record FieldCustomer(string Name, string Phone);

public sealed record FieldPlace(string Street, string City, string? AccessNotes, double Latitude, double Longitude);

public sealed record FieldEquipment(string Kind, string Brand, string Model, string SerialNumber, DateOnly InstalledOn, DateOnly? WarrantyExpiresOn);

public sealed record FieldVisit(string Number, DateTimeOffset Date, JobType Type, string Summary, string? Resolution, string? Technician);

public sealed record FieldNote(string Author, NoteSource Source, string Body, DateTimeOffset CreatedAt);

public sealed record FieldSyncRequest(List<FieldChangeRequest> Changes);

/// <param name="Id">Made on the phone when the change was made. Sending it again is safe.</param>
/// <param name="Kind"><c>start</c>, <c>complete</c> or <c>note</c>.</param>
/// <param name="MadeAt">When, by the phone's clock.</param>
/// <param name="Body">A note's text.</param>
/// <param name="Resolution">What was found and done, for a completion.</param>
public sealed record FieldChangeRequest(Guid Id, int JobId, string Kind, DateTimeOffset MadeAt, string? Body, string? Resolution, bool? FirstTimeFix);

public sealed record FieldSyncResponse(DateTimeOffset ServerTime, List<FieldChangeResult> Results);

/// <param name="Outcome"><c>Applied</c>, <c>AlreadyDone</c>, <c>Rejected</c> or <c>Invalid</c>.</param>
/// <param name="Explanation">What happened, for the technician. Null when it simply applied.</param>
public sealed record FieldChangeResult(Guid Id, string Outcome, string? Explanation);
