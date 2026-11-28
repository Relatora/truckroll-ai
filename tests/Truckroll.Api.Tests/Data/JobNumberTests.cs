using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;

namespace Truckroll.Api.Tests.Data;

public sealed class JobNumberTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_retry_moves_past_the_number_that_collided_even_if_the_database_cant_see_it_yet()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();

        // J-99999990 collided with a booking whose transaction hasn't committed, so the highest
        // committed number is still far below it. The retry must not compute the same number again.
        var next = await JobNumbers.NextAsync(db, Ct, collided: "J-99999990");

        Assert.Equal("J-99999991", next);
    }

    [Fact]
    public async Task Without_a_collision_it_is_one_past_the_highest_committed_number()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();

        var next = long.Parse((await JobNumbers.NextAsync(db, Ct))[2..], System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(next > 21000);
        Assert.True(next < 99999990);
    }
}
