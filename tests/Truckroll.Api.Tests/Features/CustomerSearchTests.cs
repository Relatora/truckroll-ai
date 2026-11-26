using System.Net.Http.Json;
using Truckroll.Api.Features.Customers;
using Truckroll.Api.Tests.Infrastructure;

namespace Truckroll.Api.Tests.Features;

public sealed class CustomerSearchTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("raman maple")]
    [InlineData("519-555-0142")]
    [InlineData("14 Maple")]
    public async Task Finds_a_customer_by_any_mix_of_name_phone_and_street(string query)
    {
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);

        var hits = await dana.GetFromJsonAsync<List<CustomerSearchResult>>($"/api/customers/search?q={Uri.EscapeDataString(query)}", Ct);

        Assert.Contains(hits!, h => h.Name == "Priya Raman" && h.Address == "14 Maple St, Guelph");
    }

    [Fact]
    public async Task Never_returns_customers_outside_the_callers_scope()
    {
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);
        using var pat = await factory.SignedInAsAsync(SignedInClients.KitchenerManager);

        var fromGuelph = await dana.GetFromJsonAsync<List<CustomerSearchResult>>("/api/customers/search?q=hartley", Ct);
        var fromKitchener = await pat.GetFromJsonAsync<List<CustomerSearchResult>>("/api/customers/search?q=hartley birch", Ct);

        Assert.All(fromGuelph!, h => Assert.Equal("GUE", h.BranchCode));
        Assert.Contains(fromKitchener!, h => h.Name == "Doug Hartley");
    }

    [Fact]
    public async Task One_letter_returns_nothing_rather_than_half_the_database()
    {
        using var morgan = await factory.SignedInAsAsync(SignedInClients.Owner);

        var hits = await morgan.GetFromJsonAsync<List<CustomerSearchResult>>("/api/customers/search?q=a", Ct);

        Assert.Empty(hits!);
    }
}
