using System.Text.RegularExpressions;

namespace Truckroll.Api.Knowledge;

/// <summary>
/// Replaces phone numbers and email addresses with <c>[phone]</c> and <c>[email]</c>. Text is
/// masked before it's embedded or stored in the index, so the index never holds contact details
/// and no model ever sees them, whichever provider answers.
/// </summary>
/// <remarks>
/// Names and street addresses are kept on purpose: "what did we install at 14 Maple St?" is the
/// question the assistant exists to answer. A phone number never helps answer anything, and a
/// prompt or trace that leaks one is a privacy incident.
/// </remarks>
public static partial class PiiMasker
{
    public const string Phone = "[phone]";
    public const string Email = "[email]";

    public static string Mask(string text) =>
        PhonePattern().Replace(EmailPattern().Replace(text, Email), Phone);

    /// <summary>The phone pattern, for <see cref="PiiVault"/>.</summary>
    internal static Regex Phones => PhonePattern();

    /// <summary>The email pattern, for <see cref="PiiVault"/>.</summary>
    internal static Regex Emails => EmailPattern();

    /// <summary>
    /// North American numbers as people write them: <c>519-555-0142</c>, <c>(519) 555-0142</c>,
    /// <c>519.555.0142</c>, <c>519 555 0142</c>, <c>5195550142</c>, with an optional <c>+1</c> or
    /// <c>1-</c> in front and an optional extension after.
    /// </summary>
    /// <remarks>
    /// The lookarounds stop it matching inside identifiers: a serial like <c>CAL80-19-10234</c>, a
    /// job number like <c>J-24051</c> or a date like <c>2026-11-25</c> is never a phone number,
    /// because it doesn't start on a word boundary with ten digits in the right groups.
    /// </remarks>
    [GeneratedRegex(
        @"(?<![\w-])(?:\+?1[\s.-]?)?(?:\(\d{3}\)|\d{3})[\s.-]?\d{3}[\s.-]?\d{4}(?:\s*(?:x|ext\.?|extension)\s*\d{1,5})?(?![\w-])",
        RegexOptions.IgnoreCase)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+")]
    private static partial Regex EmailPattern();
}
