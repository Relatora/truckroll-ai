using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Truckroll.Data.Domain;

namespace Truckroll.Data;

/// <summary>
/// The CRM's records in MySQL. MySQL is the source of truth; Redis only holds derived data
/// (the retrieval index and caches) that can be rebuilt from here.
/// </summary>
/// <remarks>
/// <para>
/// Feature code should not read these sets directly for anything a user can see. It goes through
/// the Access layer in the API (#5), which adds the branch and role filters. The sets are public
/// because the seed, migrations and indexer legitimately need everything.
/// </para>
/// <para>
/// It also holds the sign-in store (<see cref="IdentityUserContext{TUser}"/>: users, logins,
/// tokens; no role tables, see <see cref="AppUser"/>).
/// </para>
/// </remarks>
public class TruckrollDbContext(DbContextOptions<TruckrollDbContext> options) : IdentityUserContext<AppUser>(options)
{
    /// <summary>The MySQL version the schema targets. Fixed, so nothing connects just to ask.</summary>
    public static readonly ServerVersion ServerVersion = new MySqlServerVersion(new Version(8, 4, 0));

    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Technician> Technicians => Set<Technician>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobNote> JobNotes => Set<JobNote>();
    public DbSet<CallRecord> CallRecords => Set<CallRecord>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<PersonalAccessToken> PersonalAccessTokens => Set<PersonalAccessToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity's own mapping first, so ours can refine it.
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasCharSet("utf8mb4");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TruckrollDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as their names, so a report or a support query reads
        // "Dispatched", not 2. The [Flags] enums (skills, certifications) stay integers,
        // because a combination like Furnaces | Boilers has no single name.
        configurationBuilder.Properties<JobStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<JobType>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<JobPriority>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<EquipmentKind>().HaveConversion<string>().HaveMaxLength(30);
        configurationBuilder.Properties<PropertyKind>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<CustomerSource>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<MembershipPlan>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<MembershipStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<NoteSource>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<CallStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<DocumentKind>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<UserRole>().HaveConversion<string>().HaveMaxLength(20);

        configurationBuilder.Properties<decimal>().HavePrecision(10, 2);

        // Without a length, MySQL strings become LONGTEXT, which can't be indexed. A sensible
        // default; long text (notes, transcripts, documents) opts out in its configuration.
        configurationBuilder.Properties<string>().HaveMaxLength(200);
    }
}
