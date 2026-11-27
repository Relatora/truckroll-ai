using System.Text.Json.Serialization;

namespace Truckroll.Eval;

/// <summary>The question file: <c>evals/retrieval.json</c>.</summary>
internal sealed record EvalFile(string About, List<EvalCase> Cases);

/// <summary>One question and the passages that would count as a right answer.</summary>
internal sealed record EvalCase(string Id, string Category, string Question, List<Expectation> Expect)
{
    /// <summary>True when the hit satisfies any one of the acceptable answers.</summary>
    public bool IsAnsweredBy(Hit hit) => Expect.Any(e => e.Matches(hit));
}

/// <summary>
/// What a right answer looks like. Every field that's set must hold. Answers are described by
/// content (a document code, a heading, words in the text), never by record id, so the eval
/// survives the demo data being rebuilt.
/// </summary>
internal sealed record Expectation(string? Source, string? Id, string? Title, List<string>? Text)
{
    public bool Matches(Hit hit) =>
        (Source is null || string.Equals(Source, hit.Source, StringComparison.OrdinalIgnoreCase))
        && (Id is null || hit.Identifiers.Contains(Id, StringComparer.OrdinalIgnoreCase))
        && (Title is null || hit.Title.Contains(Title, StringComparison.OrdinalIgnoreCase))
        && (Text is null || Text.All(t => hit.Text.Contains(t, StringComparison.OrdinalIgnoreCase)));
}

/// <summary>A search result, as <c>GET /api/knowledge/search</c> returns it.</summary>
internal sealed record Hit(
    string Record,
    string Source,
    string Title,
    string Text,
    List<string> Identifiers,
    double Score,
    int? VectorRank,
    int? KeywordRank,
    int? IdentifierRank);

/// <summary>Where one question's first right answer came, per mode (null when not in the top results).</summary>
internal sealed record CaseResult(EvalCase Case, string Question, Dictionary<string, int?> RankByMode, Dictionary<string, double> MillisecondsByMode);

[JsonSerializable(typeof(EvalFile))]
[JsonSerializable(typeof(List<Hit>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
internal sealed partial class EvalJson : JsonSerializerContext;
