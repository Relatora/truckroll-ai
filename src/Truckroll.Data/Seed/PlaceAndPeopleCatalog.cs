using Certs = Truckroll.Data.Domain.Certifications;
using Skills = Truckroll.Data.Domain.TechnicianSkills;

namespace Truckroll.Data.Seed;

/// <summary>
/// Branches, streets and names for the generated half of the demo data.
/// </summary>
/// <remarks>
/// The towns are real, so the dispatch map shows a sensible spread across a region. The
/// company, addresses and people are invented, and generated coordinates only approximate a
/// neighbourhood, so a pin never marks a real house on purpose.
/// </remarks>
internal static class PlaceAndPeopleCatalog
{
    public static readonly IReadOnlyList<BranchSeed> Branches =
    [
        new("KIT", "Kitchener", "410 Ottawa St S", 43.4370, -80.4730, "519-555-0101", Customers: 68),
        new("GUE", "Guelph", "88 Woodlawn Rd W", 43.5600, -80.2650, "519-555-0102", Customers: 63),
        new("CAM", "Cambridge", "215 Hespeler Rd", 43.3800, -80.3200, "519-555-0103", Customers: 58),
        new("BRA", "Brantford", "37 Lynden Rd", 43.1580, -80.2560, "519-555-0104", Customers: 53),
        new("STR", "Stratford", "12 Erie St", 43.3650, -80.9800, "519-555-0105", Customers: 48),
    ];

    /// <summary>
    /// The region's area codes. Every phone number is <c>555-01xx</c>, the block reserved for
    /// fiction, so with four area codes there are 400 numbers to hand out, each used once.
    /// </summary>
    public static readonly string[] AreaCodes = ["519", "226", "548", "382"];

    public static readonly string[] StreetNames =
    [
        "Maple St", "Oak Ave", "Birch Cres", "Willow Dr", "Cedar Rd", "Elm St", "Pine Ridge Rd",
        "Chestnut Lane", "Lakeview Dr", "Riverbend Rd", "Orchard Way", "Meadowbrook Cres",
        "Hillcrest Ave", "Sunset Blvd", "Fairway Rd", "Bayberry Crt", "Juniper Pl", "Cherry Hill Dr",
        "Foxglove Way", "Heritage Dr", "Kingfisher Cres", "Sandpiper Lane", "Stonegate Dr",
        "Thornbury Ave", "Whitetail Crt", "Ashwood Dr", "Brookside Rd", "Clover Lane",
    ];

    public static readonly string[] FirstNames =
    [
        "Olivia", "Liam", "Emma", "Noah", "Ava", "Ethan", "Sophia", "Lucas", "Mia", "Benjamin",
        "Chloe", "Jacob", "Zoe", "Owen", "Hannah", "Arjun", "Priya", "Wei", "Mei", "Mohammed",
        "Fatima", "Diego", "Sofia", "Kwame", "Amara", "Daniel", "Grace", "Samuel", "Leah", "Nathan",
        "Claire", "Ryan", "Megan", "Tyler", "Brooke", "Marcus", "Natalie", "Aiden", "Jasmine", "Kevin",
        "Linda", "Gary", "Brenda", "Doug", "Karen", "Tom", "Sandra", "Bill", "Heather", "Raj",
    ];

    public static readonly string[] LastNames =
    [
        "Smith", "Brown", "Tremblay", "Martin", "Roy", "Wilson", "MacDonald", "Gagnon", "Johnson",
        "Taylor", "Campbell", "Anderson", "Leblanc", "Lee", "Patel", "Singh", "Chen", "Wong", "Nguyen",
        "Kim", "Ali", "Khan", "Garcia", "Rodriguez", "Okafor", "Mensah", "Fraser", "Murray", "Stewart",
        "Clark", "Young", "Walker", "Hall", "Wright", "Scott", "Thompson", "White", "Harris", "Kowalski",
        "Novak", "Schneider", "Becker", "Van Dyk", "De Jong", "Hartley", "Raman", "Boateng", "Moreau",
    ];

    /// <summary>
    /// The technicians, written by hand so their skills and licences make dispatch interesting:
    /// every branch has someone gas-licensed, not everyone holds a 313A, and a couple of people
    /// cover two branches' worth of specialities.
    /// </summary>
    public static readonly IReadOnlyList<TechnicianSeed> Technicians =
    [
        new("KIT", "Marco", "Bellini", Skills.Furnaces | Skills.Boilers | Skills.WaterHeaters | Skills.Installations, Certs.GasG1 | Certs.GasG2 | Certs.GasG3, "#2563eb", 2014),
        new("KIT", "Aisha", "Rahman", Skills.AirConditioning | Skills.HeatPumps | Skills.Furnaces, Certs.Refrigeration313A | Certs.GasG2 | Certs.GasG3, "#db2777", 2018),
        new("KIT", "Jordan", "Price", Skills.Plumbing | Skills.Drains | Skills.WaterHeaters, Certs.Plumber306A | Certs.GasG3, "#0d9488", 2021),
        new("GUE", "Nadia", "Kowalczyk", Skills.Furnaces | Skills.WaterHeaters | Skills.IndoorAirQuality, Certs.GasG2 | Certs.GasG3, "#7c3aed", 2016),
        new("GUE", "Ben", "Okoro", Skills.AirConditioning | Skills.HeatPumps | Skills.Installations, Certs.Refrigeration313A, "#ea580c", 2019),
        new("GUE", "Tyler", "Fischer", Skills.Furnaces | Skills.AirConditioning, Certs.GasG3, "#65a30d", 2024),
        new("CAM", "Rosa", "Delgado", Skills.Furnaces | Skills.Boilers | Skills.HeatPumps, Certs.GasG2 | Certs.GasG3 | Certs.Refrigeration313A, "#0891b2", 2015),
        new("CAM", "Kyle", "Morrison", Skills.Plumbing | Skills.Drains, Certs.Plumber306A, "#ca8a04", 2020),
        new("BRA", "Dev", "Sharma", Skills.Furnaces | Skills.AirConditioning | Skills.WaterHeaters, Certs.GasG2 | Certs.GasG3 | Certs.Refrigeration313A, "#dc2626", 2017),
        new("BRA", "Emily", "Tran", Skills.IndoorAirQuality | Skills.AirConditioning, Certs.None, "#9333ea", 2023),
        new("STR", "Paul", "Vandermeer", Skills.Furnaces | Skills.Boilers | Skills.WaterHeaters | Skills.Plumbing, Certs.GasG2 | Certs.GasG3 | Certs.Plumber306A, "#16a34a", 2012),
        new("STR", "Hana", "Sato", Skills.AirConditioning | Skills.HeatPumps | Skills.Installations, Certs.Refrigeration313A, "#e11d48", 2022),
    ];
}

internal sealed record BranchSeed(
    string Code, string Name, string Street, double Latitude, double Longitude, string Phone, int Customers);

internal sealed record TechnicianSeed(
    string BranchCode, string FirstName, string LastName, Domain.TechnicianSkills Skills, Domain.Certifications Certifications, string Color, int HiredYear);
