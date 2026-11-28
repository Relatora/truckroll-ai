using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Truckroll.Data;

/// <summary>
/// Hands out human-facing job numbers (J-21857) from a counter row in the database
/// (<see cref="Domain.Counter"/>). Numbering is company-wide, which is why it lives here rather
/// than in feature code behind the Access layer.
/// </summary>
/// <remarks>
/// <para>
/// This used to be "the highest number in use, plus one". Two bookings at the same moment read
/// the same highest number, so the unique index rejected one and it retried, and under load every
/// loser computed the same next number again, in lock-step, until one ran out of tries (#50).
/// A counter can't hand the same value out twice, so there's nothing to retry.
/// </para>
/// <para>
/// Numbers are unique and increasing, not gapless: a booking that takes a number and then rolls
/// back leaves it unused, as <c>AUTO_INCREMENT</c> does. Nothing reads meaning into a gap.
/// </para>
/// </remarks>
public static class JobNumbers
{
    /// <summary>The <see cref="Domain.Counter"/> row job numbers come from.</summary>
    public const string CounterName = "JobNumber";

    /// <summary>The counter's value in a database with no jobs: the first job is J-21001.</summary>
    public const long Start = 21000;

    /// <summary>
    /// Takes the next number. It runs on a connection of its own from <paramref name="dataSource"/>,
    /// outside whatever transaction the caller has open, so the counter row is locked for this one
    /// statement only. Approving a call books its job inside a bigger transaction; if the increment
    /// joined that, every other booking in the company would wait for the approval to commit.
    /// </summary>
    public static async Task<string> NextAsync(MySqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        // LAST_INSERT_ID(expr) stores expr for this connection and returns it from the next
        // LAST_INSERT_ID(), so the value read back is the one this UPDATE wrote, never another
        // connection's. Autocommit releases the row lock as soon as the UPDATE finishes.
        command.CommandText = """
            UPDATE Counters SET Value = LAST_INSERT_ID(Value + 1) WHERE Name = @name;
            SELECT LAST_INSERT_ID();
            """;
        command.Parameters.AddWithValue("@name", CounterName);

        var next = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        if (next == 0)
        {
            // LAST_INSERT_ID() is 0 when the UPDATE matched no row: the migration never ran.
            throw new InvalidOperationException($"The {CounterName} counter row is missing; apply the migrations.");
        }

        return $"J-{next}";
    }

    /// <summary>
    /// Moves the counter up to the highest number already on a job, never down. The demo seeder
    /// numbers its own jobs and calls this afterwards.
    /// </summary>
    public static Task CatchUpAsync(TruckrollDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(CatchUpSql, cancellationToken);

    /// <summary>
    /// The statement behind <see cref="CatchUpAsync"/>. It runs only while nothing else is
    /// booking (seeding), so it reads <c>Jobs</c> without worrying about bookings in flight.
    /// </summary>
    private const string CatchUpSql = $"""
        UPDATE Counters c
        JOIN (SELECT COALESCE(MAX(CAST(SUBSTRING(Number, 3) AS UNSIGNED)), 0) AS Highest
              FROM Jobs WHERE Number REGEXP '^J-[0-9]+$') j
        SET c.Value = GREATEST(c.Value, j.Highest)
        WHERE c.Name = '{CounterName}'
        """;
}
