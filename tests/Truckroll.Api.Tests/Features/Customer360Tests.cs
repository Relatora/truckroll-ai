using System.Net.Http.Json;
using Truckroll.Api.Features.Customers;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Features;

/// <summary>The Customer 360, checked against the anchor customers whose histories are known.</summary>
public sealed class Customer360Tests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<CustomerDetail> GetAsync(string email, string lastName)
    {
        using var client = await factory.SignedInAsAsync(email);
        var id = await factory.CustomerIdAsync(lastName);
        return (await client.GetFromJsonAsync<CustomerDetail>($"/api/customers/{id}", JsonOptions.Api, Ct))!;
    }

    [Fact]
    public async Task Priya_Ramans_home_shows_what_was_installed_and_what_went_wrong()
    {
        var priya = await GetAsync(SignedInClients.GuelphCsr, "Raman");

        var home = Assert.Single(priya.Properties);
        Assert.Equal("14 Maple St", home.Street);
        Assert.Equal(3, home.Equipment.Count);

        var furnace = Assert.Single(home.Equipment, e => e.Kind == EquipmentKind.Furnace);
        Assert.Equal("CAL-80-100", furnace.Model);
        Assert.Equal(WarrantyState.Covered, furnace.Warranty);
        Assert.InRange(furnace.WarrantyLeft!.Value, 0.3, 0.5); // about four of ten years left
        Assert.Equal("M-CAL-80", furnace.ManualCode);

        Assert.Equal(MembershipPlan.ComfortClubPlus, priya.Membership!.Plan);
        Assert.Equal(2, priya.Timeline.Count(j => j.Status == JobStatus.Completed && j.Summary.Contains("not lighting", StringComparison.Ordinal)));
        Assert.Contains(priya.Timeline, j => j.Priority == JobPriority.Emergency && j.Status == JobStatus.Scheduled);
        Assert.True(priya.Stats.Visits >= 3);
        Assert.Equal(1, priya.Stats.OpenJobs);
    }

    [Fact]
    public async Task The_timeline_is_newest_first_and_carries_the_technicians_notes()
    {
        var priya = await GetAsync(SignedInClients.GuelphCsr, "Raman");

        Assert.Equal(priya.Timeline.Select(j => j.WindowStart).OrderDescending(), priya.Timeline.Select(j => j.WindowStart));
        Assert.Contains(priya.Timeline.SelectMany(j => j.Notes), n => n.Source == NoteSource.Technician && n.Body.Contains("IGN-80H", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_technician_sees_only_the_visits_they_made()
    {
        var asNadia = await GetAsync(SignedInClients.GuelphTechnician, "Raman");
        var asDana = await GetAsync(SignedInClients.GuelphCsr, "Raman");

        Assert.NotEmpty(asNadia.Timeline);
        Assert.All(asNadia.Timeline, j => Assert.Equal("Nadia Kowalczyk", j.Technician));
        Assert.True(asDana.Timeline.Count > asNadia.Timeline.Count);
    }

    [Fact]
    public async Task Equipment_installed_by_someone_else_years_ago_is_never_shown_as_covered()
    {
        var doug = await GetAsync(SignedInClients.KitchenerManager, "Hartley");

        var heater = Assert.Single(doug.Properties.Single().Equipment, e => e.Kind == EquipmentKind.WaterHeater);
        Assert.False(heater.InstalledByUs);
        Assert.Contains(heater.Warranty, new[] { WarrantyState.Expired, WarrantyState.Unknown });
        Assert.True(heater.AgeYears >= 10);
    }

    [Theory]
    [InlineData(null, WarrantyState.Unknown)]
    [InlineData("2026-11-24", WarrantyState.Expired)]
    [InlineData("2026-11-25", WarrantyState.Expiring)]
    [InlineData("2027-02-23", WarrantyState.Expiring)]
    [InlineData("2027-02-24", WarrantyState.Covered)]
    public void Warranty_state_turns_on_the_company_date(string? expires, WarrantyState expected)
    {
        var equipment = new Equipment
        {
            Brand = "Calderon",
            Model = "CAL-80-80",
            SerialNumber = "x",
            WarrantyExpiresOn = expires is null ? null : DateOnly.Parse(expires, System.Globalization.CultureInfo.InvariantCulture),
        };

        Assert.Equal(expected, CustomerEndpoints.WarrantyOf(equipment, new DateOnly(2026, 11, 25)));
    }
}
