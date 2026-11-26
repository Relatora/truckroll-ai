using System.Globalization;
using System.Security.Claims;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Access;

/// <summary>
/// The one place that decides who can see what. Every query for CRM data that a person (or an AI
/// client acting for them) can see starts here.
/// </summary>
/// <remarks>
/// <para>
/// Endpoints ask the scope for a pre-filtered query (<c>scope.Customers(db)</c>) instead of
/// reading <c>db.Customers</c>. Retrieval (#12) and every MCP tool (#16) use the same scope, so the
/// assistant can never show someone a record they couldn't open on a screen. A convention test
/// (<c>AccessConventionTests</c>) fails the build if feature code reads the DbSets directly.
/// </para>
/// <para>
/// The filters stay inside the query, so MySQL does the filtering and nothing out of scope is
/// ever loaded into memory. They also fail closed: a user with a missing branch or technician
/// claim sees nothing, rather than everything.
/// </para>
/// </remarks>
public sealed class AccessScope
{
    private AccessScope(string? userId, string displayName, UserRole? role, int? branchId, int? technicianId)
    {
        UserId = userId;
        DisplayName = displayName;
        Role = role;
        BranchId = branchId;
        TechnicianId = technicianId;
    }

    /// <summary>The scope of someone who isn't signed in: sees nothing.</summary>
    public static AccessScope Anonymous { get; } = new(null, "", null, null, null);

    public string? UserId { get; }
    public string DisplayName { get; }
    public UserRole? Role { get; }
    public int? BranchId { get; }
    public int? TechnicianId { get; }

    public bool IsAuthenticated => Role is not null;
    public bool IsOwner => Role == UserRole.Owner;

    /// <summary>Can assign and reassign jobs on the dispatch board.</summary>
    public bool CanDispatch => Role is UserRole.Owner or UserRole.BranchManager or UserRole.Dispatcher;

    /// <summary>Can review call summaries and approve the jobs and follow-ups drafted from them.</summary>
    public bool CanReviewCalls => Role is UserRole.Owner or UserRole.BranchManager or UserRole.Dispatcher or UserRole.Csr;

    /// <summary>Can book new jobs.</summary>
    public bool CanBookJobs => Role is UserRole.Owner or UserRole.BranchManager or UserRole.Dispatcher or UserRole.Csr;

    /// <summary>Can see revenue and the owner dashboard.</summary>
    public bool CanSeeFinancials => Role is UserRole.Owner or UserRole.BranchManager;

    /// <summary>Builds the scope from a signed-in principal's claims (see <see cref="TruckrollClaims"/>).</summary>
    public static AccessScope FromPrincipal(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true
            || !Enum.TryParse<UserRole>(principal.FindFirstValue(ClaimTypes.Role), out var role))
        {
            return Anonymous;
        }

        return new AccessScope(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            principal.FindFirstValue(TruckrollClaims.DisplayName) ?? "",
            role,
            ParseId(principal.FindFirstValue(TruckrollClaims.BranchId)),
            ParseId(principal.FindFirstValue(TruckrollClaims.TechnicianId)));
    }

    /// <summary>
    /// Builds the scope straight from a user record: at sign-in, before the cookie exists, and for
    /// an MCP token's owner (#16), who never has a cookie at all.
    /// </summary>
    public static AccessScope ForUser(AppUser user) =>
        new(user.Id, user.DisplayName, user.Role, user.BranchId, user.TechnicianId);

    // ------------------------------------------------------------------ scoped queries

    public IQueryable<Branch> Branches(TruckrollDbContext db) => Role switch
    {
        UserRole.Owner => db.Branches,
        not null when BranchId is { } branch => db.Branches.Where(b => b.Id == branch),
        _ => db.Branches.Where(_ => false),
    };

    /// <summary>Technicians in the user's branch (all of them for the owner).</summary>
    public IQueryable<Technician> Technicians(TruckrollDbContext db) => Role switch
    {
        UserRole.Owner => db.Technicians,
        not null when BranchId is { } branch => db.Technicians.Where(t => t.BranchId == branch),
        _ => db.Technicians.Where(_ => false),
    };

    /// <summary>
    /// Customers the user may see. Office roles see their branch's; a technician sees only the
    /// customers whose homes they've been sent to.
    /// </summary>
    public IQueryable<Customer> Customers(TruckrollDbContext db) => Role switch
    {
        UserRole.Owner => db.Customers,
        UserRole.Technician when TechnicianId is { } tech => db.Customers.Where(c => c.Jobs.Any(j => j.TechnicianId == tech)),
        UserRole.BranchManager or UserRole.Dispatcher or UserRole.Csr when BranchId is { } branch => db.Customers.Where(c => c.BranchId == branch),
        _ => db.Customers.Where(_ => false),
    };

    /// <summary>Jobs the user may see. A technician sees only their own.</summary>
    public IQueryable<Job> Jobs(TruckrollDbContext db) => Role switch
    {
        UserRole.Owner => db.Jobs,
        UserRole.Technician when TechnicianId is { } tech => db.Jobs.Where(j => j.TechnicianId == tech),
        UserRole.BranchManager or UserRole.Dispatcher or UserRole.Csr when BranchId is { } branch => db.Jobs.Where(j => j.BranchId == branch),
        _ => db.Jobs.Where(_ => false),
    };

    /// <summary>Homes belonging to customers the user may see.</summary>
    public IQueryable<Property> Properties(TruckrollDbContext db)
    {
        var customers = Customers(db).Select(c => c.Id);
        return db.Properties.Where(p => customers.Contains(p.CustomerId));
    }

    /// <summary>Equipment in homes the user may see.</summary>
    public IQueryable<Equipment> Equipment(TruckrollDbContext db)
    {
        var properties = Properties(db).Select(p => p.Id);
        return db.Equipment.Where(e => properties.Contains(e.PropertyId));
    }

    /// <summary>Notes on jobs the user may see.</summary>
    public IQueryable<JobNote> JobNotes(TruckrollDbContext db)
    {
        var jobs = Jobs(db).Select(j => j.Id);
        return db.JobNotes.Where(n => jobs.Contains(n.JobId));
    }

    /// <summary>Calls to the user's branch. Technicians don't handle calls.</summary>
    public IQueryable<CallRecord> Calls(TruckrollDbContext db) => Role switch
    {
        UserRole.Owner => db.CallRecords,
        UserRole.BranchManager or UserRole.Dispatcher or UserRole.Csr when BranchId is { } branch => db.CallRecords.Where(c => c.BranchId == branch),
        _ => db.CallRecords.Where(_ => false),
    };

    /// <summary>Company-wide documents, plus the user's own branch's procedures.</summary>
    public IQueryable<KnowledgeDocument> Documents(TruckrollDbContext db) => Role switch
    {
        UserRole.Owner => db.KnowledgeDocuments,
        not null => db.KnowledgeDocuments.Where(d => d.BranchId == null || d.BranchId == BranchId),
        _ => db.KnowledgeDocuments.Where(_ => false),
    };

    private static int? ParseId(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
}
