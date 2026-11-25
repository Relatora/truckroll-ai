namespace Truckroll.Data.Domain;

/// <summary>
/// A home where the company has worked. Equipment belongs to a property, not a customer, because
/// the furnace stays when the house is sold.
/// </summary>
public sealed class Property
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public required string Street { get; set; }
    public required string City { get; set; }
    public required string PostalCode { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public PropertyKind Kind { get; set; }
    public int? YearBuilt { get; set; }

    /// <summary>Gate codes, dogs, where the furnace is. Shown to the technician, never indexed.</summary>
    public string? AccessNotes { get; set; }

    public List<Equipment> Equipment { get; set; } = [];
    public List<Job> Jobs { get; set; } = [];

    /// <summary>One line, the way a dispatcher reads it out: "14 Maple St, Guelph".</summary>
    public string ShortAddress => $"{Street}, {City}";
}

public enum PropertyKind
{
    Detached,
    SemiDetached,
    Townhouse,
    Bungalow,
    Cottage,
}
