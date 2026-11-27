using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Truckroll.Api.Access;
using Truckroll.Api.Common;
using Truckroll.Api.Features.Jobs;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Notifications;

/// <summary>
/// When a job is assigned, drafts the "your technician is booked" message for the customer.
/// A draft only: it waits for a person, like every follow-up in Truckroll.
/// </summary>
/// <remarks>
/// <para>
/// The first subscriber that isn't part of the request that made the change. With Kafka it's a
/// consumer group of its own (<c>notifications</c>), so it keeps its place in the stream through
/// restarts and catches up on anything it missed while it was down.
/// </para>
/// <para>
/// It must not draft twice. Delivery is at least once, so the same "assigned" event can arrive
/// again after a crash; the event id is unique in the table, and a second delivery is a no-op.
/// </para>
/// </remarks>
public sealed class TechnicianAssignedDrafter(IServiceScopeFactory scopes, ILogger<TechnicianAssignedDrafter> logger) : IJobChangeHandler
{
    public string Subscription => "notifications";

    public async Task HandleAsync(JobChanged change, CancellationToken cancellationToken)
    {
        if (change.Type != "job.assigned" || change.EventId == Guid.Empty)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();

        if (await db.CustomerNotifications.AnyAsync(n => n.EventId == change.EventId, cancellationToken))
        {
            logger.LogInformation("Event {EventId} already drafted a notification; skipping the redelivery", change.EventId);
            return;
        }

        // The system scope: this runs for every branch, on no one's behalf. The draft is only
        // visible to people who can see the job.
        var job = await AccessScope.System.Jobs(db)
            .Include(j => j.Customer).Include(j => j.Technician).Include(j => j.Branch)
            .SingleOrDefaultAsync(j => j.Id == change.JobId, cancellationToken);
        if (job?.Technician is null)
        {
            return; // Unassigned again, or gone, before we got to it: nothing true to say.
        }

        db.Add(Draft(job, change.EventId));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (IsDuplicate(db, change.EventId))
        {
            // Two deliveries raced past the check above; the unique index let exactly one in.
        }
    }

    private static bool IsDuplicate(TruckrollDbContext db, Guid eventId)
    {
        db.ChangeTracker.Clear();
        return db.CustomerNotifications.Any(n => n.EventId == eventId);
    }

    internal static CustomerNotification Draft(Job job, Guid eventId)
    {
        var start = TimeZoneInfo.ConvertTime(job.WindowStart, CompanyClock.Zone);
        var end = TimeZoneInfo.ConvertTime(job.WindowEnd, CompanyClock.Zone);
        var culture = CultureInfo.GetCultureInfo("en-CA");
        var day = start.ToString("dddd, MMMM d", culture);
        var window = $"between {Hour(start)} and {Hour(end)}";

        return new CustomerNotification
        {
            EventId = eventId,
            JobId = job.Id,
            CustomerId = job.CustomerId,
            Kind = "technician-assigned",
            Subject = $"Your visit on {day}: {job.Technician!.FirstName} is booked",
            // The window sits mid-sentence, so "2 p.m." never collides with a full stop.
            Body = $"Hi {job.Customer.FirstName}, {job.Technician.FullName} from Kestrel Home Comfort will arrive {window} " +
                   $"on {day}. We'll call if anything changes. " +
                   $"Questions? Call {job.Branch.Name} at {job.Branch.Phone}. Job {job.Number}.",
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static string Hour(DateTimeOffset time) => time.Hour switch
    {
        0 => "midnight",
        12 => "noon",
        < 12 => $"{time.Hour} a.m.",
        _ => $"{time.Hour - 12} p.m.",
    };
}
