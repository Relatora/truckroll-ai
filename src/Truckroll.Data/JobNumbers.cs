using Microsoft.EntityFrameworkCore;

namespace Truckroll.Data;

/// <summary>
/// Hands out the next human-facing job number (J-21857). Numbering is company-wide, so it reads
/// every job, which is why it lives here rather than in feature code behind the Access layer.
/// </summary>
public static class JobNumbers
{
    /// <summary>
    /// The next number after the highest in use. Two bookings at the same instant can both get the
    /// same answer; the unique index on Number turns that into a failed save, and the caller
    /// retries (see JobEndpoints.BookAsync).
    /// </summary>
    public static async Task<string> NextAsync(TruckrollDbContext db, CancellationToken cancellationToken)
    {
        var highest = await db.Database
            .SqlQuery<long?>($"SELECT MAX(CAST(SUBSTRING(Number, 3) AS UNSIGNED)) AS Value FROM Jobs WHERE Number LIKE 'J-%'")
            .SingleAsync(cancellationToken);

        return $"J-{(highest ?? 21000) + 1}";
    }
}
