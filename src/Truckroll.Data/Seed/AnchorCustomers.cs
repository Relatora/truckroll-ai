using Truckroll.Data.Domain;

namespace Truckroll.Data.Seed;

/// <summary>
/// Hand-written customers the demo script, the call transcripts and the evals refer to by name.
/// </summary>
/// <remarks>
/// Generated data is good for volume and bad for storytelling. These homes have exact histories
/// so a question like "what did we install at 14 Maple St?" or "how many times has Priya Raman's
/// ignitor failed?" has one known answer. Their history is written relative to the anchor date,
/// so it stays recent whenever the demo runs.
/// </remarks>
internal static class AnchorCustomers
{
    public static readonly IReadOnlyList<AnchorCustomer> All =
    [
        new("GUE", "Priya", "Raman", "priya.raman@mail.test", "519-555-0142", CustomerSource.GoogleReviews,
            "14 Maple St", "N1H 2K7", 43.5510, -80.2580, PropertyKind.Detached, 1994,
            "Side door, furnace in the basement utility room. Friendly golden retriever.",
            Membership: MembershipPlan.ComfortClubPlus,
            Equipment:
            [
                new("CAL-80", "100", "04417", InstalledDaysAgo: 2240, InstalledByUs: true),
                new("FL-16", "30", "11873", InstalledDaysAgo: 2240, InstalledByUs: true),
                new("AVT-199", "", "00981", InstalledDaysAgo: 1310, InstalledByUs: true),
            ],
            History:
            [
                new(Equipment: 0, DaysAgo: 360, Fault: 0, "GUE"),
                new(Equipment: 2, DaysAgo: 175, Fault: 0, "GUE"),
                new(Equipment: 0, DaysAgo: 24, Fault: 0, "GUE"),
            ]),
        new("KIT", "Doug", "Hartley", "doug.hartley@inbox.test", "226-555-0118", CustomerSource.RepeatCustomer,
            "88 Birch Cres", "N2M 4R1", 43.4290, -80.4880, PropertyKind.Bungalow, 1968,
            "Boiler room through the garage. Ring twice, he's hard of hearing.",
            Membership: MembershipPlan.ComfortClub,
            Equipment:
            [
                new("HMB-110", "", "00342", InstalledDaysAgo: 3720, InstalledByUs: true),
                new("AV-50", "", "27710", InstalledDaysAgo: 4010, InstalledByUs: false),
            ],
            History:
            [
                new(Equipment: 0, DaysAgo: 330, Fault: 1, "KIT"),
                new(Equipment: 0, DaysAgo: 40, Fault: 0, "KIT"),
                new(Equipment: 1, DaysAgo: 95, Fault: 0, "KIT"),
            ]),
        new("CAM", "Mei", "Chen", "mei.chen@example.test", "548-555-0133", CustomerSource.GoogleSearch,
            "7 Juniper Pl", "N3C 1T5", 43.3870, -80.3290, PropertyKind.Townhouse, 2007,
            null,
            Membership: MembershipPlan.ComfortClub,
            Equipment:
            [
                new("FHP-20", "36", "00577", InstalledDaysAgo: 760, InstalledByUs: true),
                new("CAL-96", "60", "01822", InstalledDaysAgo: 760, InstalledByUs: true),
            ],
            History:
            [
                new(Equipment: 0, DaysAgo: 340, Fault: 0, "CAM"),
            ]),
        new("BRA", "Kwame", "Mensah", "kwame.mensah@mail.test", "519-555-0167", CustomerSource.Referral,
            "231 Lakeview Dr", "N3R 6B2", 43.1690, -80.2480, PropertyKind.Detached, 2002,
            "Basement entrance at the back. Code for the side gate is on the work order.",
            Membership: null,
            Equipment:
            [
                new("CAL-96", "80", "30155", InstalledDaysAgo: 2900, InstalledByUs: true),
                new("FL-16", "24", "09931", InstalledDaysAgo: 2900, InstalledByUs: true),
            ],
            History:
            [
                new(Equipment: 0, DaysAgo: 690, Fault: 0, "BRA"),
                new(Equipment: 1, DaysAgo: 135, Fault: 0, "BRA"),
                new(Equipment: 0, DaysAgo: 310, Fault: 0, "BRA"),
            ]),
        new("STR", "Linda", "Moreau", "linda.moreau@inbox.test", "226-555-0151", CustomerSource.YardSign,
            "52 Heritage Dr", "N5A 3X8", 43.3720, -80.9640, PropertyKind.Detached, 1988,
            null,
            Membership: MembershipPlan.ComfortClub,
            Equipment:
            [
                new("CAL-80", "80", "02260", InstalledDaysAgo: 2530, InstalledByUs: true),
                new("MH-12", "", "04471", InstalledDaysAgo: 2530, InstalledByUs: true),
            ],
            History:
            [
                new(Equipment: 0, DaysAgo: 410, Fault: 0, "STR"),
                new(Equipment: 1, DaysAgo: 300, Fault: 0, "STR"),
            ]),
        new("GUE", "Sandra", "Kowalski", "sandra.kowalski@example.test", "382-555-0109", CustomerSource.Facebook,
            "140 Riverbend Rd", "N1G 3H4", 43.5320, -80.2290, PropertyKind.SemiDetached, 1979,
            null,
            Membership: null,
            Equipment:
            [
                new("AVT-199", "", "00410", InstalledDaysAgo: 1640, InstalledByUs: true),
                new("HM-95", "60", "77810", InstalledDaysAgo: 5100, InstalledByUs: false),
            ],
            History:
            [
                new(Equipment: 0, DaysAgo: 520, Fault: 0, "GUE"),
                new(Equipment: 0, DaysAgo: 150, Fault: 1, "GUE"),
            ]),
        new("CAM", "Tom", "Becker", "tom.becker@mail.test", "519-555-0186", CustomerSource.HomeShow,
            "63 Cedar Rd", "N1R 2L9", 43.3610, -80.3110, PropertyKind.Detached, 1975,
            null,
            Membership: null,
            Equipment:
            [
                new("HM-95", "80", "41522", InstalledDaysAgo: 6200, InstalledByUs: false),
                new("AV-50", "", "11845", InstalledDaysAgo: 4300, InstalledByUs: true),
            ],
            History:
            [
                new(Equipment: 0, DaysAgo: 250, Fault: 0, "CAM"),
            ]),
    ];
}

internal sealed record AnchorCustomer(
    string BranchCode,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    CustomerSource Source,
    string Street,
    string PostalCode,
    double Latitude,
    double Longitude,
    PropertyKind Kind,
    int YearBuilt,
    string? AccessNotes,
    MembershipPlan? Membership,
    IReadOnlyList<AnchorEquipment> Equipment,
    IReadOnlyList<AnchorJob> History);

/// <param name="ModelCode">An <see cref="EquipmentCatalog"/> code, e.g. <c>CAL-80</c>.</param>
/// <param name="Size">The size variant that fills the model pattern, e.g. <c>100</c> for CAL-80-100.</param>
/// <param name="SerialSuffix">
/// The unique tail of the serial number. The generator adds the model and the year of manufacture
/// (<c>CAL80-20-04417</c>), so the year always agrees with the install date, whatever the anchor date.
/// </param>
internal sealed record AnchorEquipment(string ModelCode, string Size, string SerialSuffix, int InstalledDaysAgo, bool InstalledByUs);

/// <param name="Equipment">Index into the anchor's equipment list.</param>
/// <param name="Fault">Index into that model's <see cref="EquipmentModel.Faults"/>.</param>
/// <param name="TechnicianBranch">Which branch's qualified technician did the work.</param>
internal sealed record AnchorJob(int Equipment, int DaysAgo, int Fault, string TechnicianBranch);
