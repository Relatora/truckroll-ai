namespace Truckroll.Data.Domain;

/// <summary>
/// A token a person creates to let an AI client (Claude Desktop, over MCP) act as them. The
/// token itself is shown once and never stored: only its SHA-256 hash is, so a database leak
/// doesn't hand anyone a working token.
/// </summary>
public sealed class PersonalAccessToken
{
    public int Id { get; set; }

    public required string UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>What the person called it, usually the device: "Work laptop".</summary>
    public required string Name { get; set; }

    /// <summary>The first characters of the token (<c>trk_ab12</c>), so a person can tell tokens apart.</summary>
    public required string Prefix { get; set; }

    /// <summary>SHA-256 of the full token, as lower-case hex. Looked up on every request.</summary>
    public required string Hash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>Set when the owner revokes it. A revoked token fails on its very next request.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsLive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
