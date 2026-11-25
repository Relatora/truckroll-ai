namespace Truckroll.Data.Domain;

/// <summary>
/// One location the company runs crews out of. Branches are the unit of access: everyone
/// except the owner works inside one, and most screens are filtered to it.
/// </summary>
public sealed class Branch
{
    public int Id { get; set; }

    /// <summary>Short code used in job numbers and on the dispatch board, e.g. <c>KIT</c>.</summary>
    public required string Code { get; set; }

    /// <summary>Display name, e.g. "Kitchener".</summary>
    public required string Name { get; set; }

    public required string Street { get; set; }
    public required string City { get; set; }
    public required string Phone { get; set; }

    /// <summary>The shop's location: where trucks start the day and the map centres.</summary>
    public double Latitude { get; set; }

    /// <inheritdoc cref="Latitude"/>
    public double Longitude { get; set; }

    public List<Technician> Technicians { get; set; } = [];
    public List<Customer> Customers { get; set; } = [];
}
