using System.Net.Http.Json;
using Truckroll.Api.Features.Customers;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Features;

public sealed class CustomerListTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<CustomerPage> ListAsync(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<CustomerPage>($"/api/customers?{query}", JsonOptions.Api, Ct))!;

    [Fact]
    public async Task An_office_role_lists_only_its_branch()
    {
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);

        var page = await ListAsync(dana, "pageSize=100");

        Assert.True(page.Total > 50);
        Assert.All(page.Items, c => Assert.Equal("GUE", c.BranchCode));
    }

    [Fact]
    public async Task Branch_filters_offer_only_branches_in_scope()
    {
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);
        using var morgan = await factory.SignedInAsAsync(SignedInClients.Owner);

        var hers = await dana.GetFromJsonAsync<List<Truckroll.Api.Features.Branches.BranchListItem>>("/api/branches", Ct);
        var his = await morgan.GetFromJsonAsync<List<Truckroll.Api.Features.Branches.BranchListItem>>("/api/branches", Ct);

        Assert.Equal("GUE", Assert.Single(hers!).Code);
        // The schema tests add throwaway branches to the shared database, so check the five are there.
        Assert.Superset(new HashSet<string> { "KIT", "GUE", "CAM", "BRA", "STR" }, his!.Select(b => b.Code).ToHashSet());
    }

    [Fact]
    public async Task Pages_do_not_overlap_and_cover_the_total()
    {
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);

        var first = await ListAsync(dana, "pageSize=10&page=1");
        var second = await ListAsync(dana, "pageSize=10&page=2");

        Assert.Equal(10, first.Items.Count);
        Assert.Empty(first.Items.Select(c => c.Id).Intersect(second.Items.Select(c => c.Id)));
        Assert.Equal(first.Total, second.Total);
    }

    [Fact]
    public async Task The_owner_can_narrow_to_one_branch()
    {
        using var morgan = await factory.SignedInAsAsync(SignedInClients.Owner);

        var kitchener = await ListAsync(morgan, "branch=KIT&pageSize=100");
        var everyone = await ListAsync(morgan, "pageSize=5");

        Assert.All(kitchener.Items, c => Assert.Equal("KIT", c.BranchCode));
        Assert.True(everyone.Total > kitchener.Total);
    }

    [Fact]
    public async Task Membership_filters_split_the_list_cleanly()
    {
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);

        var all = await ListAsync(dana, "pageSize=5");
        var active = await ListAsync(dana, "membership=active&pageSize=100");
        var lapsed = await ListAsync(dana, "membership=lapsed&pageSize=100");
        var none = await ListAsync(dana, "membership=none&pageSize=100");

        Assert.All(active.Items, c => Assert.Equal(MembershipStatus.Active, c.MembershipStatus));
        Assert.All(none.Items, c => Assert.Null(c.Plan));
        Assert.Equal(all.Total, active.Total + lapsed.Total + none.Total);
    }

    [Fact]
    public async Task Sorting_by_last_visit_puts_the_most_recent_first()
    {
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);

        var page = await ListAsync(dana, "sort=lastVisit&pageSize=20");
        var visits = page.Items.Where(c => c.LastVisit is not null).Select(c => c.LastVisit!.Value).ToList();

        Assert.Equal(visits.OrderDescending(), visits);
    }

    [Fact]
    public async Task Search_works_inside_the_list_too()
    {
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);

        var page = await ListAsync(dana, "q=maple%20raman");

        var priya = Assert.Single(page.Items);
        Assert.Equal("14 Maple St, Guelph", priya.Address);
        Assert.Equal(MembershipPlan.ComfortClubPlus, priya.Plan);
    }
}
