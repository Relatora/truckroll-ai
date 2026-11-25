namespace Truckroll.Data.Domain;

/// <summary>
/// A maintenance plan: a monthly fee for two tune-ups a year, priority booking and a discount.
/// </summary>
/// <remarks>
/// Memberships are recurring revenue and the best predictor of a repeat customer, which is why
/// renewals are one of the owner dashboard's headline numbers (#17).
/// </remarks>
public sealed class Membership
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public MembershipPlan Plan { get; set; }
    public MembershipStatus Status { get; set; }

    public DateOnly StartedOn { get; set; }
    public DateOnly RenewsOn { get; set; }

    public decimal MonthlyPrice { get; set; }
}

public enum MembershipPlan
{
    /// <summary>Two tune-ups a year and priority booking.</summary>
    ComfortClub,

    /// <summary>Adds no-charge diagnostics and 15% off repairs.</summary>
    ComfortClubPlus,
}

public enum MembershipStatus
{
    Active,
    Lapsed,
    Cancelled,
}
