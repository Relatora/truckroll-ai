using System.Globalization;
using Truckroll.Data.Domain;
using Truckroll.Data.Seed;

namespace Truckroll.Api.Tests.Data;

/// <summary>
/// The demo data is part of the product: the demo script, the evals and the screenshots all
/// depend on it. These tests pin down the promises it makes.
/// </summary>
public sealed class DemoDataTests
{
    private static readonly DateOnly Today = new(2026, 11, 25);
    private static readonly DemoData Data = new DemoDataGenerator(DemoSeeder.DefaultSeed, Today).Generate();

    [Fact]
    public void The_same_seed_and_date_always_produce_identical_data()
    {
        var again = new DemoDataGenerator(DemoSeeder.DefaultSeed, Today).Generate();

        Assert.Equal(Fingerprint(Data), Fingerprint(again));
    }

    [Fact]
    public void A_different_seed_produces_different_data()
    {
        var other = new DemoDataGenerator(DemoSeeder.DefaultSeed + 1, Today).Generate();

        Assert.NotEqual(Fingerprint(Data), Fingerprint(other));
    }

    [Fact]
    public void Volume_matches_the_design()
    {
        Assert.Equal(5, Data.Branches.Count);
        Assert.Equal(12, Data.Technicians.Count);
        Assert.InRange(Data.Customers.Count, 290, 310);
        Assert.InRange(Data.Jobs.Count, 800, 2500);
        Assert.Equal(6, Data.Calls.Count);
    }

    [Fact]
    public void Contact_details_are_fictional_and_unique()
    {
        Assert.All(Data.Customers, c => Assert.EndsWith(".test", c.Email, StringComparison.Ordinal));
        Assert.All(Data.Customers, c => Assert.Contains("-555-01", c.Phone, StringComparison.Ordinal));
        Assert.Equal(Data.Customers.Count, Data.Customers.Select(c => c.Phone).Distinct().Count());
    }

    [Fact]
    public void Every_equipment_model_has_a_manual()
    {
        var models = Data.Customers.SelectMany(c => c.Properties).SelectMany(p => p.Equipment)
            .Select(e => Data.Documents.SingleOrDefault(d => d.Kind == DocumentKind.Manual && e.Model.StartsWith(d.AppliesToModel!, StringComparison.Ordinal)))
            .ToList();

        Assert.DoesNotContain(null, models);
    }

    [Fact]
    public void Serial_numbers_are_unique_and_carry_the_year_of_manufacture()
    {
        var equipment = Data.Customers.SelectMany(c => c.Properties).SelectMany(p => p.Equipment).ToList();

        Assert.Equal(equipment.Count, equipment.Select(e => e.SerialNumber).Distinct().Count());
        Assert.All(equipment, e => Assert.Contains($"-{e.InstalledOn.Year % 100:00}-", e.SerialNumber, StringComparison.Ordinal));
    }

    [Fact]
    public void Calderon_CAL80s_from_2019_to_2021_fail_far_more_often_than_other_furnaces()
    {
        // The pattern the assistant should find when asked "which furnace models keep failing?"
        var furnaceRepairs = Data.Jobs.Where(j => j.Type == JobType.Repair && j.Equipment?.Kind == EquipmentKind.Furnace).ToList();
        var furnaces = Data.Customers.SelectMany(c => c.Properties).SelectMany(p => p.Equipment).Where(e => e.Kind == EquipmentKind.Furnace).ToList();

        static bool InBulletinRange(Equipment e) => e.Model.StartsWith("CAL-80", StringComparison.Ordinal) && e.InstalledOn.Year is >= 2019 and <= 2021;

        var affected = furnaces.Where(InBulletinRange).ToList();
        var others = furnaces.Where(e => !InBulletinRange(e)).ToList();
        var affectedRate = (double)furnaceRepairs.Count(j => InBulletinRange(j.Equipment!)) / affected.Count;
        var otherRate = (double)furnaceRepairs.Count(j => !InBulletinRange(j.Equipment!)) / others.Count;

        Assert.True(affected.Count >= 20, $"Only {affected.Count} affected furnaces; the pattern needs volume to show.");
        Assert.True(affectedRate > otherRate * 1.5, $"Affected rate {affectedRate:F2} vs others {otherRate:F2}.");
    }

    [Fact]
    public void Priya_Ramans_ignitor_has_failed_twice_and_today_is_the_third_time()
    {
        var priya = Data.AnchorCustomers.Single(c => c.LastName == "Raman");
        var furnace = priya.Properties[0].Equipment.Single(e => e.Kind == EquipmentKind.Furnace);
        var furnaceJobs = Data.Jobs.Where(j => j.Equipment == furnace).OrderBy(j => j.WindowStart).ToList();

        Assert.Equal("14 Maple St", priya.Properties[0].Street);
        Assert.StartsWith("CAL80-20-", furnace.SerialNumber, StringComparison.Ordinal);
        Assert.Equal(2, furnaceJobs.Count(j => j.Status == JobStatus.Completed && j.Summary.Contains("not lighting", StringComparison.Ordinal)));

        var today = Assert.Single(furnaceJobs, j => j.Status != JobStatus.Completed);
        Assert.Equal(JobPriority.Emergency, today.Priority);
        Assert.Null(today.Technician);
        Assert.Equal(Certifications.GasG2, today.RequiredCertification);
    }

    [Fact]
    public void Todays_board_is_a_late_morning_snapshot_in_every_branch()
    {
        var zone = DemoDataGenerator.LocalZone;
        var todays = Data.Jobs.Where(j => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(j.WindowStart, zone).DateTime) == Today).ToList();

        Assert.All(Data.Branches, branch =>
        {
            var board = todays.Where(j => j.Branch == branch).ToList();
            Assert.Contains(board, j => j.Status == JobStatus.Completed);
            Assert.Contains(board, j => j.Status == JobStatus.InProgress);
            Assert.Contains(board, j => j.Status == JobStatus.Scheduled && j.Technician is null);
        });
    }

    [Fact]
    public void Technicians_only_get_work_they_are_licensed_for()
    {
        Assert.All(Data.Jobs.Where(j => j.Technician is not null), job =>
            Assert.Equal(job.RequiredCertification, job.Technician!.Certifications & job.RequiredCertification));
    }

    [Fact]
    public void Job_numbers_are_unique_and_follow_booking_order()
    {
        Assert.Equal(Data.Jobs.Count, Data.Jobs.Select(j => j.Number).Distinct().Count());

        var byNumber = Data.Jobs.OrderBy(j => int.Parse(j.Number[2..], CultureInfo.InvariantCulture)).Select(j => j.CreatedAt).ToList();
        Assert.Equal(byNumber.Order().ToList(), byNumber);
    }

    /// <summary>A text rendering of everything that matters, without database ids.</summary>
    private static string Fingerprint(DemoData data) => string.Join('\n',
        data.Customers.Select(c => $"{c.FullName}|{c.Email}|{c.Phone}|{c.Branch.Code}|{string.Join(',', c.Properties.SelectMany(p => p.Equipment).Select(e => e.SerialNumber))}")
            .Concat(data.Jobs.Select(j => $"{j.Number}|{j.Summary}|{j.WindowStart:O}|{j.Status}|{j.Technician?.FullName}|{j.InvoiceTotal}"))
            .Concat(data.Documents.Select(d => $"{d.Code}|{d.Body.Length}"))
            .Concat(data.Calls.Select(c => $"{c.Branch.Code}|{c.ReceivedAt:O}")));
}
