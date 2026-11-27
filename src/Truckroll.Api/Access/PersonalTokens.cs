using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Access;

/// <summary>
/// Personal access tokens: how an AI client acts as a person over MCP. Created, listed and
/// revoked by their owner; checked by <see cref="PersonalTokenHandler"/> on every request.
/// </summary>
public sealed class PersonalTokens(TruckrollDbContext db, TimeProvider time)
{
    public const string TokenPrefix = "trk_";
    public const int MaxLiveTokens = 10;
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    /// <summary>
    /// SHA-256, lower-case hex. A fast hash is the right one here: a token is 256 random bits, so
    /// unlike a password there's nothing to guess, and the lookup runs on every MCP call.
    /// </summary>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public Task<List<PersonalAccessToken>> ListAsync(string userId, CancellationToken cancellationToken) =>
        db.PersonalAccessTokens.AsNoTracking().Where(t => t.UserId == userId).OrderByDescending(t => t.CreatedAt).ToListAsync(cancellationToken);

    /// <summary>Creates a token and returns it with its plain value, the only time that value exists outside the client.</summary>
    public async Task<(PersonalAccessToken Record, string Token)?> CreateAsync(string userId, string name, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var live = await db.PersonalAccessTokens.CountAsync(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now, cancellationToken);
        if (live >= MaxLiveTokens)
        {
            return null;
        }

        var token = TokenPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var record = new PersonalAccessToken
        {
            UserId = userId,
            Name = name.Trim(),
            Prefix = token[..8],
            Hash = Hash(token),
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        };
        db.PersonalAccessTokens.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return (record, token);
    }

    /// <summary>Revokes one of the user's own tokens. Returns false if it isn't theirs.</summary>
    public async Task<PersonalAccessToken?> RevokeAsync(string userId, int id, CancellationToken cancellationToken)
    {
        var token = await db.PersonalAccessTokens.SingleOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken);
        if (token is null)
        {
            return null;
        }

        token.RevokedAt ??= time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return token;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Authenticates <c>Authorization: Bearer trk_…</c>. The token's hash is looked up on every
/// request, never cached, so revoking a token (or deleting its owner) takes effect on the very
/// next call. A good token signs the request in as its owner, with the same claims a browser
/// sign-in gets, so <see cref="AccessScope"/> applies unchanged.
/// </summary>
public sealed class PersonalTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TruckrollDbContext db,
    IUserClaimsPrincipalFactory<AppUser> claims,
    TimeProvider time) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "PersonalToken";

    /// <summary>Added to the principal so actions can say they came through a token.</summary>
    public const string TokenIdClaim = "truckroll:token";

    /// <summary>"Last used" is shown to the owner; writing it on every call would be wasted I/O.</summary>
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var raw = header["Bearer ".Length..].Trim();
        if (!raw.StartsWith(PersonalTokens.TokenPrefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.Fail("Not a Truckroll token.");
        }

        var hash = PersonalTokens.Hash(raw);
        var token = await db.PersonalAccessTokens.Include(t => t.User).SingleOrDefaultAsync(t => t.Hash == hash, Context.RequestAborted);
        var now = time.GetUtcNow();
        if (token is null || !token.IsLive(now))
        {
            // One answer for unknown, revoked and expired: the caller learns nothing about which.
            return AuthenticateResult.Fail("The token is unknown, revoked or expired.");
        }

        if (token.LastUsedAt is null || now - token.LastUsedAt > LastUsedResolution)
        {
            token.LastUsedAt = now;
            await db.SaveChangesAsync(Context.RequestAborted);
        }

        var principal = await claims.CreateAsync(token.User);
        var identity = new ClaimsIdentity(principal.Claims, SchemeName);
        identity.AddClaim(new Claim(TokenIdClaim, token.Id.ToString(CultureInfo.InvariantCulture)));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer realm=\"truckroll\"";
        return Task.CompletedTask;
    }
}
