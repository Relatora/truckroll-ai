using Microsoft.EntityFrameworkCore;
using Truckroll.Data.Domain;

namespace Truckroll.Data;

/// <summary>
/// Calendar checks that have to see a technician's whole day, in every branch. A dispatcher in
/// Guelph can't read Kitchener's jobs, but they must not double-book a Kitchener technician who's
/// covering for them, so this lives below the Access layer and returns only what the check needs.
/// </summary>
public static class Schedules
{
    /// <summary>The number of a job that overlaps the window for this technician, or null if free.</summary>
    public static Task<string?> FindClashAsync(
        TruckrollDbContext db, int technicianId, DateTimeOffset start, DateTimeOffset end, int exceptJobId, CancellationToken cancellationToken) =>
        db.Jobs
            .Where(j => j.TechnicianId == technicianId && j.Id != exceptJobId && j.Status != JobStatus.Cancelled)
            .Where(j => j.WindowStart < end && j.WindowEnd > start)
            .Select(j => j.Number)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Each technician's jobs in a time range, in every branch, without the job being placed.
    /// Only what timing and travel need: no customer, no notes. Dispatch suggestions use it so a
    /// technician covering for another branch isn't treated as free when they aren't.
    /// </summary>
    public static async Task<ILookup<int, DaySlot>> DayOfAsync(
        TruckrollDbContext db, IReadOnlyCollection<int> technicianIds, DateTimeOffset from, DateTimeOffset to, int exceptJobId, CancellationToken cancellationToken)
    {
        var slots = await db.Jobs
            .Where(j => j.TechnicianId != null && technicianIds.Contains(j.TechnicianId.Value) && j.Id != exceptJobId && j.Status != JobStatus.Cancelled)
            .Where(j => j.WindowStart < to && j.WindowEnd > from)
            .Select(j => new DaySlot(j.TechnicianId!.Value, j.Number, j.WindowStart, j.WindowEnd, j.EstimatedMinutes, j.Property.Latitude, j.Property.Longitude, j.Property.Street))
            .ToListAsync(cancellationToken);
        return slots.ToLookup(s => s.TechnicianId);
    }
}

/// <summary>One job on a technician's day, as scheduling sees it.</summary>
public sealed record DaySlot(int TechnicianId, string Number, DateTimeOffset WindowStart, DateTimeOffset WindowEnd, int EstimatedMinutes, double Latitude, double Longitude, string Street);
