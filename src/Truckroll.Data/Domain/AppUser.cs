using Microsoft.AspNetCore.Identity;

namespace Truckroll.Data.Domain;

/// <summary>
/// Someone who signs in: office staff, a manager, the owner, or a technician.
/// </summary>
/// <remarks>
/// One role and at most one branch, as plain columns. Five roles with fixed meanings don't need
/// Identity's many-to-many role tables, and the Access layer reads these values from claims
/// either way (see <c>Truckroll.Api/Access/AccessScope.cs</c>).
/// </remarks>
public sealed class AppUser : IdentityUser
{
    /// <summary>How the person is shown in the app and on notes they write: "Dana Whitfield".</summary>
    public required string DisplayName { get; set; }

    public UserRole Role { get; set; }

    /// <summary>The branch they work in. Null only for the owner, who sees every branch.</summary>
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }

    /// <summary>For technicians: their record on the dispatch board, which scopes them to their own jobs.</summary>
    public int? TechnicianId { get; set; }
    public Technician? Technician { get; set; }
}

/// <summary>What someone does, which decides what they can see and do.</summary>
public enum UserRole
{
    /// <summary>Runs the company. Sees every branch and the financials.</summary>
    Owner,

    /// <summary>Runs one branch. Sees all of it, including its financials.</summary>
    BranchManager,

    /// <summary>Routes technicians for one branch. Owns the dispatch board.</summary>
    Dispatcher,

    /// <summary>Customer service rep: answers calls and books jobs for one branch.</summary>
    Csr,

    /// <summary>In the field. Sees their own jobs and the customers on them.</summary>
    Technician,
}
