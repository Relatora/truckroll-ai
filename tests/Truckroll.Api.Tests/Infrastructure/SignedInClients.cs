using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Truckroll.Data;
using Truckroll.Data.Seed;

namespace Truckroll.Api.Tests.Infrastructure;

/// <summary>Helpers for tests that act as one of the demo users.</summary>
internal static class SignedInClients
{
    public const string Owner = "morgan@kestrel.test";
    public const string KitchenerManager = "pat@kestrel.test";
    public const string GuelphDispatcher = "alex@kestrel.test";
    public const string GuelphCsr = "dana@kestrel.test";
    public const string GuelphTechnician = "nadia@kestrel.test";

    /// <summary>A client with its own cookie jar, signed in through the real endpoint.</summary>
    public static async Task<HttpClient> SignedInAsAsync(this TruckrollApiFactory factory, string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password = DemoSeeder.DemoPassword }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Looks up a demo customer's id by last name, bypassing access rules (test setup only).</summary>
    public static async Task<int> CustomerIdAsync(this TruckrollApiFactory factory, string lastName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();
        return await db.Customers.Where(c => c.LastName == lastName && c.Email.EndsWith("test")).Select(c => c.Id).FirstAsync(TestContext.Current.CancellationToken);
    }
}
