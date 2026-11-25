namespace Truckroll.Data.Domain;

/// <summary>
/// A homeowner (or landlord) the company serves. Belongs to the branch that serves their area.
/// </summary>
public sealed class Customer
{
    public int Id { get; set; }

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public required string FirstName { get; set; }
    public required string LastName { get; set; }

    /// <summary>
    /// Contact details are personal data. They never reach a model unmasked (see AI_WORKFLOW.md),
    /// and the seed only ever generates <c>.test</c> emails and 555 numbers.
    /// </summary>
    public required string Email { get; set; }

    /// <inheritdoc cref="Email"/>
    public required string Phone { get; set; }

    /// <summary>How they found us: a marketing question the owner dashboard answers.</summary>
    public CustomerSource Source { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<Property> Properties { get; set; } = [];
    public List<Job> Jobs { get; set; } = [];
    public Membership? Membership { get; set; }

    public string FullName => $"{FirstName} {LastName}";
}

public enum CustomerSource
{
    Referral,
    GoogleSearch,
    GoogleReviews,
    Facebook,
    YardSign,
    TruckWrap,
    HomeShow,
    RepeatCustomer,
}
