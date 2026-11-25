using Truckroll.Data.Domain;

namespace Truckroll.Data.Seed;

/// <summary>
/// The invented equipment line-up, and the faults each model is known for.
/// </summary>
/// <remarks>
/// Every brand here is made up. The faults are not: they're the common, seasonal failures a
/// residential HVAC and plumbing shop really sees, so the generated history has the shape of
/// the real thing. A few are deliberately over-represented (see <see cref="Fault.Weight"/>) to
/// plant patterns the assistant should be able to find and explain.
/// </remarks>
internal static class EquipmentCatalog
{
    // Declared before Models on purpose: static fields initialise in order, and Models uses these.
    private static readonly int[] Winter = [10, 11, 12, 1, 2, 3];
    private static readonly int[] Summer = [6, 7, 8];
    private static readonly int[] AllYear = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

    public static readonly IReadOnlyList<EquipmentModel> Models =
    [
        new(EquipmentKind.Furnace, "Calderon", "CAL-80", "CAL-80-{0}", WarrantyYears: 10, Certifications.GasG2, TechnicianSkills.Furnaces,
        [
            // The planted pattern: units built 2019 to 2021 crack hot-surface ignitors (SB-2024-07).
            new("Furnace not lighting, blower runs but no heat", "Hot surface ignitor cracked. Replaced with IGN-80H and tested five cycles.",
                "Ignitor cracked again, hairline crack at the base. Part IGN-80H. Unit is in the SB-2024-07 serial range, flagged it for the warranty claim.",
                Minutes: 75, Low: 285, High: 340, FirstTimeFix: 0.9, Weight: 6, Months: Winter),
            new("Furnace short-cycles, shuts off after a few minutes", "Flame sensor coated. Cleaned with fine emery cloth, verified microamp reading.",
                "Flame sensor reading 0.8 µA before cleaning, 4.1 after. Told homeowner to book the fall tune-up.",
                Minutes: 45, Low: 169, High: 199, FirstTimeFix: 0.95, Weight: 3, Months: Winter),
            new("Three flashes on furnace status light", "Pressure switch tubing cracked. Replaced tubing, cleared the code.",
                "3-flash code, pressure switch not closing. Tubing split at the elbow. Replaced, tested.",
                Minutes: 60, Low: 189, High: 245, FirstTimeFix: 0.85, Weight: 2, Months: Winter),
        ]),
        new(EquipmentKind.Furnace, "Calderon", "CAL-96", "CAL-96-{0}", WarrantyYears: 10, Certifications.GasG2, TechnicianSkills.Furnaces,
        [
            // Condensing furnaces: condensate traps freeze or clog in cold snaps.
            new("High-efficiency furnace keeps shutting off, gurgling sound", "Condensate trap clogged. Cleared and flushed trap, checked drain line slope.",
                "Trap full of sludge, pressure switch tripping on it. Flushed, re-primed trap. Drain line runs uphill near the floor drain, recommended re-routing.",
                Minutes: 60, Low: 195, High: 260, FirstTimeFix: 0.8, Weight: 4, Months: Winter),
            new("Water pooling under the furnace", "Condensate line disconnected at the pump. Reconnected and secured.",
                "Condensate pump hose slipped off. Secured with a clamp. No damage to the control board.",
                Minutes: 40, Low: 150, High: 185, FirstTimeFix: 0.95, Weight: 2, Months: Winter),
            new("Furnace blowing cold air", "Inducer motor failing. Replaced inducer assembly.",
                "Inducer bearing noisy and slow to start. Replaced IND-96 assembly, tested static pressure.",
                Minutes: 120, Low: 520, High: 690, FirstTimeFix: 0.6, Weight: 1, Months: Winter),
        ]),
        new(EquipmentKind.Furnace, "Hearthmark", "HM-95", "HM-95-{0}", WarrantyYears: 10, Certifications.GasG2, TechnicianSkills.Furnaces,
        [
            new("No heat, thermostat calling", "Blown 3 A fuse on the control board from a shorted thermostat wire. Replaced fuse and repaired wire.",
                "Thermostat wire chewed at the joist (mice). Spliced, replaced 3 A fuse.",
                Minutes: 55, Low: 165, High: 210, FirstTimeFix: 0.9, Weight: 2, Months: Winter),
            new("Furnace making loud squealing noise", "Blower belt worn. Replaced belt, adjusted tension.",
                "Belt glazed and cracked. Replaced, lubricated blower bearings.",
                Minutes: 45, Low: 140, High: 180, FirstTimeFix: 0.95, Weight: 1, Months: Winter),
        ]),
        new(EquipmentKind.AirConditioner, "Frostline", "FL-16", "FL-16-{0}", WarrantyYears: 10, Certifications.Refrigeration313A, TechnicianSkills.AirConditioning,
        [
            // Heat waves kill run capacitors.
            new("AC outdoor unit humming but fan not spinning", "Run capacitor failed. Replaced 45/5 µF dual capacitor.",
                "Capacitor bulged, reading 31 µF on a 45 µF rating. Replaced. Amp draw normal after.",
                Minutes: 50, Low: 225, High: 290, FirstTimeFix: 0.95, Weight: 5, Months: Summer),
            new("AC running but house not cooling", "Low refrigerant from a leak at the service valve. Repaired leak, recharged R-454B.",
                "Subcooling 2°F, leak found at the suction service valve core. Replaced core, recharged 1.5 lb.",
                Minutes: 110, Low: 420, High: 610, FirstTimeFix: 0.7, Weight: 2, Months: Summer),
            new("Ice on the AC lines", "Dirty filter and low airflow. Replaced filter, thawed coil, checked charge.",
                "Filter completely blocked. Coil iced. Thawed, new filter, charge fine.",
                Minutes: 60, Low: 160, High: 199, FirstTimeFix: 0.9, Weight: 2, Months: Summer),
        ]),
        new(EquipmentKind.HeatPump, "Frostline", "FHP-20", "FHP-20-{0}", WarrantyYears: 10, Certifications.Refrigeration313A, TechnicianSkills.HeatPumps,
        [
            new("Heat pump outdoor unit covered in ice", "Defrost sensor failed. Replaced sensor and verified defrost cycle.",
                "Coil iced solid, unit never entering defrost. Defrost thermistor open. Replaced DS-20.",
                Minutes: 80, Low: 290, High: 360, FirstTimeFix: 0.85, Weight: 2, Months: Winter),
            new("Heat pump blowing lukewarm air", "Reversing valve sticking. Freed valve with controlled cycling, monitored.",
                "Reversing valve slow to shift. Cycled it, works now. Watch it; may need replacement.",
                Minutes: 90, Low: 210, High: 280, FirstTimeFix: 0.6, Weight: 1, Months: Winter),
        ]),
        new(EquipmentKind.WaterHeater, "Aquavane", "AV-50", "AV-50-{0}", WarrantyYears: 6, Certifications.GasG2, TechnicianSkills.WaterHeaters,
        [
            new("No hot water, pilot keeps going out", "Thermocouple failed. Replaced thermocouple and relit pilot.",
                "Thermocouple output 8 mV, needs 20+. Replaced, pilot holding.",
                Minutes: 50, Low: 175, High: 220, FirstTimeFix: 0.9, Weight: 2, Months: AllYear),
            new("Water heater leaking from the bottom", "Tank failed. Quoted replacement; customer approved AV-50 install.",
                "Tank rusted through at the bottom seam. Not repairable. Replacement quote left with homeowner.",
                Minutes: 45, Low: 120, High: 150, FirstTimeFix: 0.3, Weight: 1, Months: AllYear),
        ]),
        new(EquipmentKind.TanklessWaterHeater, "Aquavane", "AVT-199", "AVT-199-{0}", WarrantyYears: 12, Certifications.GasG2, TechnicianSkills.WaterHeaters,
        [
            // Hard water scales the heat exchanger and the flow sensor (SB-2025-03).
            new("Tankless water heater showing E-12, hot water cuts out", "Heat exchanger scaled. Descaled with vinegar flush, cleaned inlet filter.",
                "E-12 code, scale on the heat exchanger. 45-minute descale flush. Hard water area; recommended a softener and yearly flush.",
                Minutes: 90, Low: 230, High: 280, FirstTimeFix: 0.9, Weight: 4, Months: AllYear),
            new("Hot water lukewarm at the taps", "Flow sensor fouled with scale. Cleaned flow sensor and inlet screen.",
                "Flow sensor reading low. Scale on the turbine. Cleaned. See SB-2025-03 about the updated sensor.",
                Minutes: 70, Low: 190, High: 240, FirstTimeFix: 0.8, Weight: 2, Months: AllYear),
        ]),
        new(EquipmentKind.Boiler, "Hearthmark", "HMB-110", "HMB-110-{0}", WarrantyYears: 10, Certifications.GasG2, TechnicianSkills.Boilers,
        [
            new("Boiler pressure keeps dropping", "Expansion tank waterlogged. Recharged tank, repaired a leaking relief valve.",
                "System pressure falling to 0.5 bar overnight. Expansion tank had no air charge. Recharged to 12 psi.",
                Minutes: 90, Low: 260, High: 340, FirstTimeFix: 0.8, Weight: 2, Months: Winter),
            new("Radiators cold upstairs", "Air in the system. Bled radiators and topped up pressure.",
                "Upstairs rads full of air. Bled all, topped to 1.2 bar.",
                Minutes: 50, Low: 150, High: 185, FirstTimeFix: 0.95, Weight: 1, Months: Winter),
        ]),
        new(EquipmentKind.Humidifier, "Mistral", "MH-12", "MH-12-{0}", WarrantyYears: 5, Certifications.None, TechnicianSkills.IndoorAirQuality,
        [
            new("Humidifier not adding moisture", "Water panel scaled and solenoid valve clogged. Replaced panel, cleaned solenoid.",
                "Panel hard as rock. Replaced WP-12 and cleaned the solenoid strainer.",
                Minutes: 35, Low: 110, High: 145, FirstTimeFix: 0.95, Weight: 1, Months: Winter),
        ]),
        new(EquipmentKind.AirExchanger, "Breezeway", "BW-150", "BW-150-{0}", WarrantyYears: 5, Certifications.None, TechnicianSkills.IndoorAirQuality,
        [
            new("Air exchanger rattling", "Fan wheel out of balance from debris. Cleaned core and fan.",
                "Core clogged with lint, fan wheel caked. Cleaned both, balanced.",
                Minutes: 45, Low: 130, High: 165, FirstTimeFix: 0.9, Weight: 1, Months: AllYear),
        ]),
    ];

    /// <summary>The model a seeded equipment record of this kind should use, picked by the generator.</summary>
    public static IEnumerable<EquipmentModel> OfKind(EquipmentKind kind) => Models.Where(m => m.Kind == kind);

    public static EquipmentModel ByCode(string code) => Models.Single(m => m.Code == code);

}

/// <summary>One equipment model: what it is, which licence and skill its work needs, and how it fails.</summary>
/// <param name="ModelPattern">The full model number, with <c>{0}</c> for the size variant.</param>
internal sealed record EquipmentModel(
    EquipmentKind Kind,
    string Brand,
    string Code,
    string ModelPattern,
    int WarrantyYears,
    Certifications Certification,
    TechnicianSkills Skill,
    IReadOnlyList<Fault> Faults);

/// <summary>A failure a model is known for, with how it presents, how it's fixed, and what it costs.</summary>
/// <param name="Symptom">What the customer reports. Becomes the job summary.</param>
/// <param name="Resolution">What the technician did. Becomes the job resolution.</param>
/// <param name="Note">The technician's note, in the clipped way techs write.</param>
/// <param name="Weight">How often this fault is picked relative to the model's others.</param>
/// <param name="Months">The months it happens in (furnaces fail in winter, ACs in heat waves).</param>
internal sealed record Fault(
    string Symptom,
    string Resolution,
    string Note,
    int Minutes,
    decimal Low,
    decimal High,
    double FirstTimeFix,
    int Weight,
    int[] Months);
