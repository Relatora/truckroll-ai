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
}
