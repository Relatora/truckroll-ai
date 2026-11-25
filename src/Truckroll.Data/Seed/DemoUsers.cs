using Microsoft.AspNetCore.Identity;
using Truckroll.Data.Domain;

namespace Truckroll.Data.Seed;

/// <summary>
/// One demo login per role. The README lists them; the password is <see cref="DemoSeeder.DemoPassword"/>.
/// </summary>
/// <remarks>
/// Dana and the other office names match the people answering the phone in the seeded call
/// transcripts, so signing in as Dana and opening this morning's calls reads like her real day.
/// Unlike the rest of the seed these aren't deterministic: password hashes and security stamps are
/// random by design, and nothing compares them.
/// </remarks>
internal static class DemoUsers
{
    public static IEnumerable<AppUser> Create(DemoData data)
    {
        Branch Branch(string code) => data.Branches.Single(b => b.Code == code);

        var nadia = data.Technicians.Single(t => t.FirstName == "Nadia");

        AppUser[] users =
        [
            New("morgan@kestrel.test", "Morgan Reid", UserRole.Owner, branch: null),
            New("pat@kestrel.test", "Pat Gallagher", UserRole.BranchManager, Branch("KIT")),
            New("alex@kestrel.test", "Alex Romero", UserRole.Dispatcher, Branch("GUE")),
            New("dana@kestrel.test", "Dana Whitfield", UserRole.Csr, Branch("GUE")),
            New("nadia@kestrel.test", nadia.FullName, UserRole.Technician, nadia.Branch, nadia),
        ];

        var hasher = new PasswordHasher<AppUser>();
        foreach (var user in users)
        {
            user.PasswordHash = hasher.HashPassword(user, DemoSeeder.DemoPassword);
        }

        return users;
    }

    private static AppUser New(string email, string name, UserRole role, Branch? branch, Technician? technician = null) => new()
    {
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true,
        DisplayName = name,
        Role = role,
        Branch = branch,
        Technician = technician,
        SecurityStamp = Guid.NewGuid().ToString("N"),
    };
}
