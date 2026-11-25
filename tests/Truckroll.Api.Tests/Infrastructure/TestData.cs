using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Infrastructure;

/// <summary>
/// Small, explicit object graphs for tests that need a few rows, not the whole demo seed.
/// Every value that must be unique takes a suffix, because all tests share one database.
/// </summary>
internal static class TestData
{
    private static int _sequence;

    public static Job NewJob(TruckrollDbContext db, string suffix)
    {
        var n = Interlocked.Increment(ref _sequence);
        var unique = $"{suffix}{n}";

        var branch = new Branch
        {
            Code = $"T{n:000}",
            Name = $"Test branch {unique}",
            Street = "1 Test Rd",
            City = "Testville",
            Phone = "555-0100",
            Latitude = 43.45,
            Longitude = -80.49,
        };

        var technician = new Technician
        {
            Branch = branch,
            FirstName = "Tess",
            LastName = $"Tech{unique}",
            Skills = TechnicianSkills.Furnaces | TechnicianSkills.Boilers | TechnicianSkills.WaterHeaters,
            Certifications = Certifications.GasG3 | Certifications.GasG2,
            Color = "#2563eb",
            HiredOn = new DateOnly(2021, 4, 12),
        };

        var customer = new Customer
        {
            Branch = branch,
            FirstName = "Casey",
            LastName = $"Customer{unique}",
            Email = $"casey.{unique.ToLowerInvariant()}@example.test",
            Phone = "555-0142",
            Source = CustomerSource.Referral,
            CreatedAt = DateTimeOffset.UtcNow,
            Membership = new Membership
            {
                Plan = MembershipPlan.ComfortClubPlus,
                Status = MembershipStatus.Active,
                StartedOn = new DateOnly(2025, 1, 1),
                RenewsOn = new DateOnly(2027, 1, 1),
                MonthlyPrice = 29.99m,
            },
        };

        var property = new Property
        {
            Customer = customer,
            Street = "14 Maple St",
            City = "Testville",
            PostalCode = "N2A 1A1",
            Latitude = 43.46,
            Longitude = -80.50,
            Kind = PropertyKind.Detached,
            YearBuilt = 1998,
        };

        var furnace = new Equipment
        {
            Property = property,
            Kind = EquipmentKind.Furnace,
            Brand = "Calderon",
            Model = "CAL-80-96",
            SerialNumber = $"SN-{unique}",
            InstalledOn = new DateOnly(2019, 10, 3),
            WarrantyExpiresOn = new DateOnly(2029, 10, 3),
            InstalledByUs = true,
        };

        var start = new DateTimeOffset(2026, 12, 1, 13, 0, 0, TimeSpan.Zero);
        return new Job
        {
            Number = $"J-{unique}",
            Branch = branch,
            Customer = customer,
            Property = property,
            Equipment = furnace,
            Type = JobType.Repair,
            Priority = JobPriority.Urgent,
            Status = JobStatus.Dispatched,
            RequiredSkill = TechnicianSkills.Furnaces,
            RequiredCertification = Certifications.GasG2,
            WindowStart = start,
            WindowEnd = start.AddHours(2),
            EstimatedMinutes = 90,
            Technician = technician,
            Summary = "Furnace short-cycles and blows cold air.",
            InvoiceTotal = 289.50m,
            CreatedAt = start.AddDays(-1),
            DispatchedAt = start.AddHours(-2),
            Notes =
            [
                new JobNote
                {
                    Author = "Tess Tech",
                    Source = NoteSource.Technician,
                    Body = "Flame sensor dirty; cleaned and tested three cycles.",
                    CreatedAt = start.AddHours(1),
                },
            ],
        };
    }
}
