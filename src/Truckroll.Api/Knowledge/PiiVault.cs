using System.Text.RegularExpressions;

namespace Truckroll.Api.Knowledge;

/// <summary>
/// Masking that can be undone, for one request. Each phone number and email becomes a numbered
/// token (<c>[phone 1]</c>, <c>[email 1]</c>) before text goes to a model, and whatever the model
/// writes back has the tokens swapped for the originals before a person reads it.
/// </summary>
/// <remarks>
/// The search index uses <see cref="PiiMasker"/>, which throws details away for good: nobody
/// needs a phone number back from a passage. A call is different. The CSR replying to it needs
/// the caller's email in the draft, and the model never needs to see it to write that draft.
/// The originals live only in this object, for the length of one request.
/// </remarks>
public sealed partial class PiiVault
{
    private readonly Dictionary<string, string> _originals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _tokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _emails = [];

    /// <summary>The emails found so far, in the order they appeared.</summary>
    public IReadOnlyList<string> Emails => _emails;

    public string Mask(string text)
    {
        var withoutEmails = PiiMasker.Emails.Replace(text, m => Token("email", m.Value));
        return PiiMasker.Phones.Replace(withoutEmails, m => Token("phone", m.Value));
    }

    /// <summary>Puts the originals back. Tokens the vault never issued are left as they are.</summary>
    public string Restore(string text) =>
        TokenPattern().Replace(text, m => _originals.TryGetValue(m.Value, out var original) ? original : m.Value);

    private string Token(string kind, string value)
    {
        // The same number twice in a call gets the same token, so the model can tell they match.
        if (_tokens.TryGetValue(value, out var existing))
        {
            return existing;
        }

        var token = $"[{kind} {_originals.Keys.Count(k => k.StartsWith($"[{kind} ", StringComparison.Ordinal)) + 1}]";
        _tokens[value] = token;
        _originals[token] = value;
        if (kind == "email")
        {
            _emails.Add(value);
        }

        return token;
    }

    [GeneratedRegex(@"\[(phone|email) \d+\]")]
    private static partial Regex TokenPattern();
}
