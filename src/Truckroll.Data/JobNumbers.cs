using Microsoft.EntityFrameworkCore;

namespace Truckroll.Data;

/// <summary>
/// Hands out the next human-facing job number (J-21857). Numbering is company-wide, so it reads
/// every job, which is why it lives here rather than in feature code behind the Access layer.
/// </summary>
public static class JobNumbers
{
    /// <summary>
    /// The next number after the highest in use, and after <paramref name="collided"/> when given.
    /// Two bookings at the same instant can both get the same answer; the unique index on Number
    /// turns that into a failed save, and the caller retries (see JobEndpoints.BookAsync).
    /// </summary>
    /// <remarks>
    /// The retry passes the number that just collided, and that matters. The highest number this
    /// query sees is the highest committed one. A booking inside a longer transaction (approving a
    /// call books the job and then does more) holds its number uncommitted, so without the floor
    /// every retry computes the same next number and collides with it again (found by the gate
    /// under full parallel load, #22).
    /// </remarks>
    public static async Task<string> NextAsync(TruckrollDbContext db, CancellationToken cancellationToken, string? collided = null)
    {
        var highest = await db.Database
            .SqlQuery<long?>($"SELECT MAX(CAST(SUBSTRING(Number, 3) AS UNSIGNED)) AS Value FROM Jobs WHERE Number LIKE 'J-%'")
            .SingleAsync(cancellationToken);

        var floor = collided is { Length: > 2 } && long.TryParse(collided.AsSpan(2), out var tried) ? tried : 0;
        return $"J-{Math.Max(highest ?? 21000, floor) + 1}";
    }
}
