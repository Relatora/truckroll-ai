using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;

namespace Truckroll.Api.Tests.Access;

/// <summary>
/// The access matrix, tested from both sides through the real API: the person who should get in,
/// and the one who shouldn't. Priya Raman is a Guelph customer whose furnace Nadia has repaired;
/// Doug Hartley is in Kitchener.
/// </summary>
public sealed class CustomerAccessTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(SignedInClients.Owner, HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData(SignedInClients.KitchenerManager, HttpStatusCode.NotFound, HttpStatusCode.OK)]
    [InlineData(SignedInClients.GuelphDispatcher, HttpStatusCode.OK, HttpStatusCode.NotFound)]
    [InlineData(SignedInClients.GuelphCsr, HttpStatusCode.OK, HttpStatusCode.NotFound)]
    [InlineData(SignedInClients.GuelphTechnician, HttpStatusCode.OK, HttpStatusCode.NotFound)]
    public async Task Each_role_sees_exactly_the_customers_it_should(string email, HttpStatusCode priya, HttpStatusCode doug)
    {
        using var client = await factory.SignedInAsAsync(email);

        var priyaId = await factory.CustomerIdAsync("Raman");
        var dougId = await factory.CustomerIdAsync("Hartley");

        Assert.Equal(priya, (await client.GetAsync($"/api/customers/{priyaId}", Ct)).StatusCode);
        Assert.Equal(doug, (await client.GetAsync($"/api/customers/{dougId}", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_technician_cannot_open_a_customer_in_their_own_branch_they_have_never_visited()
    {
        int strangerId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();
            var nadia = await db.Technicians.SingleAsync(t => t.FirstName == "Nadia", Ct);
            strangerId = await db.Customers
                .Where(c => c.BranchId == nadia.BranchId && !c.Jobs.Any(j => j.TechnicianId == nadia.Id))
                .Select(c => c.Id)
                .FirstAsync(Ct);
        }

        using var technician = await factory.SignedInAsAsync(SignedInClients.GuelphTechnician);
        using var csr = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);

        Assert.Equal(HttpStatusCode.NotFound, (await technician.GetAsync($"/api/customers/{strangerId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await csr.GetAsync($"/api/customers/{strangerId}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_for_customer_data_get_401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/customers/{await factory.CustomerIdAsync("Raman")}", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
