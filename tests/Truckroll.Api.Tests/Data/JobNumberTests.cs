using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;
using Truckroll.Data.Seed;

namespace Truckroll.Api.Tests.Data;

/// <summary>The counter job numbers come from (#50).</summary>
public sealed class JobNumberTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MySqlDataSource DataSource => factory.Services.GetRequiredService<MySqlDataSource>();

    [Fact]
    public async Task Fifty_numbers_taken_at_once_are_fifty_different_numbers()
    {
        var numbers = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => JobNumbers.NextAsync(DataSource, Ct)));

        Assert.Equal(50, numbers.Distinct().Count());
        Assert.All(numbers, n => Assert.Matches("^J-[0-9]+$", n));
    }

    [Fact]
    public async Task A_number_taken_inside_a_transaction_that_rolls_back_is_not_handed_out_again()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();

        // The booking's transaction rolls back, but the number was taken on a connection of its
        // own and already committed. It stays used: a gap, never a duplicate.
        await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
        {
            var abandoned = await JobNumbers.NextAsync(DataSource, Ct);
            await transaction.RollbackAsync(Ct);

            var next = await JobNumbers.NextAsync(DataSource, Ct);
            Assert.True(Number(next) > Number(abandoned));
        }
    }

    [Fact]
    public async Task Taking_a_number_never_waits_for_a_transaction_that_is_still_open()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();

        // Approving a call takes a number and then does more before it commits. If the counter
        // row were locked by that transaction, this second booking would wait on it (and, with
        // the default 50-second lock timeout, the test's cancellation would fire first).
        await using var approval = await db.Database.BeginTransactionAsync(Ct);
        var first = await JobNumbers.NextAsync(DataSource, Ct);

        using var quick = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        quick.CancelAfter(TimeSpan.FromSeconds(5));
        var second = await JobNumbers.NextAsync(DataSource, quick.Token);

        Assert.True(Number(second) > Number(first));
        await approval.RollbackAsync(Ct);
    }

    [Fact]
    public async Task The_counter_is_ahead_of_every_number_on_a_job()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();

        var counter = await db.Counters.Where(c => c.Name == JobNumbers.CounterName).Select(c => c.Value).SingleAsync(Ct);
        var numbers = await db.Jobs.Where(j => j.Number.StartsWith("J-")).Select(j => j.Number).ToListAsync(Ct);

        // Test-made jobs carry suffixes like J-field12; only the counter's own numbers count.
        var highest = numbers.Where(n => n[2..].All(char.IsAsciiDigit)).Max(Number);
        Assert.True(counter >= highest, $"Counter at {counter}, but J-{highest} is already on a job.");
    }

    [Fact]
    public async Task On_a_database_with_history_the_migration_starts_the_counter_after_the_last_job()
    {
        // A schema of its own, seeded, then taken back to before the counter existed: the state
        // of every database that had jobs when this shipped. Applying the migration again must
        // carry on from the highest number there, not restart at J-21001.
        var options = new DbContextOptionsBuilder<TruckrollDbContext>()
            .UseMySql(factory.ConnectionStringFor("truckroll_counter_test"), TruckrollDbContext.ServerVersion)
            .Options;
        await using var db = new TruckrollDbContext(options);
        await db.Database.EnsureDeletedAsync(Ct);
        await db.Database.MigrateAsync(Ct);
        await DemoSeeder.SeedIfEmptyAsync(db, new DateOnly(2026, 11, 25), cancellationToken: Ct);

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("AddFieldChanges", cancellationToken: Ct);
        await migrator.MigrateAsync(cancellationToken: Ct);

        var highest = (await db.Jobs.Select(j => j.Number).ToListAsync(Ct)).Max(Number);
        Assert.True(highest > JobNumbers.Start);
        Assert.Equal(highest, await db.Counters.Where(c => c.Name == JobNumbers.CounterName).Select(c => c.Value).SingleAsync(Ct));
    }

    private static long Number(string number) => long.Parse(number[2..], System.Globalization.CultureInfo.InvariantCulture);
}
