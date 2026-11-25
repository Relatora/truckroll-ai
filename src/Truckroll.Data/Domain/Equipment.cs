namespace Truckroll.Data.Domain;

/// <summary>
/// One appliance at a property: a furnace, an air conditioner, a water heater.
/// </summary>
/// <remarks>
/// This is the record that answers the questions customers actually call with ("is it still
/// under warranty?", "how old is it?") and the one the assistant cites when asked "what did we
/// install at 14 Maple St?". Serial and model numbers are matched exactly by retrieval (#12).
/// </remarks>
public sealed class Equipment
{
    public int Id { get; set; }

    public int PropertyId { get; set; }
    public Property Property { get; set; } = null!;

    public EquipmentKind Kind { get; set; }

    /// <summary>Manufacturer. The seed uses invented brands, never real ones.</summary>
    public required string Brand { get; set; }

    /// <summary>Model number, e.g. <c>CAL-80-96</c>. Links to its manual by prefix.</summary>
    public required string Model { get; set; }

    public required string SerialNumber { get; set; }

    public DateOnly InstalledOn { get; set; }

    /// <summary>Parts warranty end. Null when the warranty is unknown (equipment we didn't install).</summary>
    public DateOnly? WarrantyExpiresOn { get; set; }

    /// <summary>True when we installed it, so we know its history from day one.</summary>
    public bool InstalledByUs { get; set; }

    public List<Job> Jobs { get; set; } = [];

    /// <summary>Under warranty on the given day.</summary>
    public bool IsUnderWarranty(DateOnly onDay) => WarrantyExpiresOn is { } expires && expires >= onDay;

    /// <summary>Age in whole years on the given day.</summary>
    public int AgeInYears(DateOnly onDay)
    {
        var age = onDay.Year - InstalledOn.Year;
        return onDay < InstalledOn.AddYears(age) ? age - 1 : age;
    }
}

public enum EquipmentKind
{
    Furnace,
    AirConditioner,
    HeatPump,
    Boiler,
    WaterHeater,
    TanklessWaterHeater,
    Humidifier,
    AirExchanger,
}
