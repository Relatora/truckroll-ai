namespace Truckroll.Data.Domain;

/// <summary>
/// A field technician: the person (and truck) a job is dispatched to.
/// </summary>
public sealed class Technician
{
    public int Id { get; set; }

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public required string FirstName { get; set; }
    public required string LastName { get; set; }

    /// <summary>What they're good at. Dispatch scoring rewards a match (#14).</summary>
    public TechnicianSkills Skills { get; set; }

    /// <summary>
    /// Licences they hold. Unlike skills, these are a hard filter in dispatch: a technician
    /// without a gas licence never gets a furnace job, however close they are.
    /// </summary>
    public Certifications Certifications { get; set; }

    /// <summary>Lane and map-marker colour on the dispatch board, as a hex string.</summary>
    public required string Color { get; set; }

    public DateOnly HiredOn { get; set; }
    public bool IsActive { get; set; } = true;

    public List<Job> Jobs { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}";
}

/// <summary>Kinds of work a technician is good at. Combine with <c>|</c>.</summary>
[Flags]
public enum TechnicianSkills
{
    None = 0,
    Furnaces = 1 << 0,
    AirConditioning = 1 << 1,
    HeatPumps = 1 << 2,
    Boilers = 1 << 3,
    WaterHeaters = 1 << 4,
    Plumbing = 1 << 5,
    Drains = 1 << 6,
    IndoorAirQuality = 1 << 7,

    /// <summary>Full system replacements, which need a crew lead, not just a repair tech.</summary>
    Installations = 1 << 8,
}

/// <summary>
/// Licences that legally gate work in Ontario. Combine with <c>|</c>.
/// </summary>
/// <remarks>
/// The names follow the province's real licence classes (TSSA gas technician G3/G2/G1 and the
/// 313A and 306A trades) so the domain reads true to anyone in the industry. Holding a higher
/// gas class implies the lower ones; the seed sets all of them explicitly so queries stay simple.
/// </remarks>
[Flags]
public enum Certifications
{
    None = 0,

    /// <summary>G3: the entry-level gas licence. Works under a G2 or G1 holder's supervision.</summary>
    GasG3 = 1 << 0,

    /// <summary>
    /// G2: gas appliances up to 400,000 BTU/h, which covers residential furnaces, boilers and
    /// gas water heaters. The licence most residential gas jobs require.
    /// </summary>
    GasG2 = 1 << 1,

    /// <summary>G1: all gas appliances, including large commercial ones.</summary>
    GasG1 = 1 << 2,

    /// <summary>313A refrigeration and air conditioning mechanic. Needed to handle refrigerant.</summary>
    Refrigeration313A = 1 << 3,

    /// <summary>306A plumber.</summary>
    Plumber306A = 1 << 4,
}
