using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Access;

/// <summary>Claim types Truckroll adds to a signed-in principal.</summary>
public static class TruckrollClaims
{
    public const string DisplayName = "truckroll:name";
    public const string BranchId = "truckroll:branch";
    public const string TechnicianId = "truckroll:technician";
}

/// <summary>
/// Puts the user's role, branch and technician id into the sign-in cookie as claims, so the
/// Access layer can build a scope on every request without a database round trip.
/// </summary>
/// <remarks>
/// The trade-off: a role or branch change takes effect at the user's next sign-in. Identity's
/// security stamp validation (every 30 minutes by default) re-issues the cookie, and changing a
/// user's role should also update their security stamp, which signs them out everywhere.
/// </remarks>
public sealed class TruckrollClaimsPrincipalFactory(UserManager<AppUser> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.ToString()));
        identity.AddClaim(new Claim(TruckrollClaims.DisplayName, user.DisplayName));

        if (user.BranchId is { } branch)
        {
            identity.AddClaim(new Claim(TruckrollClaims.BranchId, branch.ToString(CultureInfo.InvariantCulture)));
        }

        if (user.TechnicianId is { } technician)
        {
            identity.AddClaim(new Claim(TruckrollClaims.TechnicianId, technician.ToString(CultureInfo.InvariantCulture)));
        }

        return identity;
    }
}
