using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using StackExchange.Redis;
using Truckroll.Api.Access;
using Truckroll.Api.Ai;
using Truckroll.Data;

namespace Truckroll.Api.Knowledge;

/// <summary>Which rankings a search uses. Hybrid in the product; the others exist for the eval.</summary>
public enum SearchMode
{
    Hybrid,
    Vector,
    Keyword,
    Identifier,
}

/// <summary>One passage that answered a search, with where each ranking placed it (1 is best).</summary>
public sealed record SearchHit(
    string Record,
    PassageSource Source,
    string Title,
    string Text,
    IReadOnlyList<string> Identifiers,
    int? CustomerId,
    double Score,
    int? VectorRank,
    int? KeywordRank,
    int? IdentifierRank);

/// <summary>
/// Hybrid retrieval: three rankings over the passage index, fused, then checked against the
/// asker's access scope. The assistant (#13) and the MCP tools (#16) both search through this.
/// </summary>
/// <remarks>
/// <para>
/// Each ranking catches what the others miss. Vector search finds paraphrases ("lights then cuts
/// out" finds "Burners light then shut off"). Keyword search (BM25) finds rare exact words
/// ("thermocouple", "microamps"). The identifier ranking matches job, serial, model and part
/// numbers exactly, which embeddings blur and keyword tokenisers split at the hyphens.
/// </para>
/// <para>
/// They're combined with weighted Reciprocal Rank Fusion: a passage scores
/// <c>Σ weight / (60 + rank)</c> over the rankings that found it. RRF uses positions, not raw
/// scores, so a cosine distance and a BM25 score never have to be put on one scale.
/// </para>
/// </remarks>
public sealed partial class KnowledgeSearch(
    IConnectionMultiplexer redis,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    TruckrollDbContext db,
    IOptions<AiOptions> options)
{
    /// <summary>How deep each ranking looks. Fusion and the access check pick from these.</summary>
    public const int CandidatesPerRanking = 40;

    /// <summary>The standard RRF constant: it damps the gap between first and second place.</summary>
    public const double RrfK = 60;

    /// <summary>
    /// How widely the HNSW graph is explored per query. Redis's default (10) is tuned for speed,
    /// and since the graph is rebuilt in memory on every Redis restart, a shallow search returned
    /// different neighbours from one restart to the next. At 200 the search is close to exact for
    /// an index this size and still takes milliseconds.
    /// </summary>
    public const int VectorSearchBreadth = 200;

    public const double VectorWeight = 1.0;
    public const double KeywordWeight = 1.0;

    /// <summary>
    /// An identifier that matches only a handful of passages (a job or serial number) is close to
    /// proof, so it outvotes the other rankings. One that matches many (a model family such as
    /// <c>CAL-80</c>) is just one more vote.
    /// </summary>
    public const double RareIdentifierWeight = 3.0;
    public const double CommonIdentifierWeight = 1.0;
    public const int RareIdentifierMatches = 10;

    /// <summary>An exact street address in the question: strong, but a home has many passages.</summary>
    public const double AddressWeight = 2.0;
    private static readonly string[] ReturnedFields = ["title", "text", "source", "record", "ids", "customer"];

    /// <summary>Question words that carry no meaning for keyword search.</summary>
    private static readonly HashSet<string> QuestionWords = new(StringComparer.Ordinal)
    {
        "a", "about", "an", "and", "any", "are", "as", "at", "be", "but", "by", "can", "could", "did", "do",
        "does", "for", "from", "get", "got", "had", "has", "have", "how", "i", "if", "in", "into", "is", "it",
        "its", "me", "my", "of", "on", "or", "our", "should", "so", "that", "the", "their", "them", "then",
        "there", "these", "they", "this", "to", "us", "was", "we", "were", "what", "when", "where", "which",
        "who", "why", "will", "with", "would", "you", "your",
    };

    /// <summary>Searches the index as <paramref name="scope"/>, returning at most <paramref name="limit"/> passages they may see.</summary>
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string question,
        AccessScope scope,
        int limit = 8,
        SearchMode mode = SearchMode.Hybrid,
        CancellationToken cancellationToken = default)
    {
        if (!scope.IsAuthenticated || string.IsNullOrWhiteSpace(question))
        {
            return [];
        }

        var filter = BranchFilter(scope);
        var found = new Dictionary<string, Document>(StringComparer.Ordinal);
        var rankings = new List<Ranking>();

        if (mode is SearchMode.Hybrid or SearchMode.Vector)
        {
            rankings.Add(await VectorRankingAsync(question, filter, found, cancellationToken));
        }

        var words = KeywordTerms(question);
        if (mode is SearchMode.Hybrid or SearchMode.Keyword && words.Count > 0)
        {
            rankings.Add(await RunAsync("keyword", KeywordWeight, new Query($"{filter} @title|text:({string.Join('|', words)})"), found));
        }

        var identifiers = IdentifiersIn(question);
        if (mode is SearchMode.Hybrid or SearchMode.Identifier && identifiers.Count > 0)
        {
            rankings.Add(await IdentifierRankingAsync(identifiers, words, filter, found));
        }

        // A street address names one home the way a serial names one unit: "14 Maple St" must not
        // be out-voted by every other Maple St. An exact phrase match is its own ranking.
        var addresses = AddressesIn(question);
        if (mode is SearchMode.Hybrid or SearchMode.Identifier && addresses.Count > 0)
        {
            var phrases = string.Join('|', addresses.Select(a => $"\"{a}\""));
            rankings.Add(await RunAsync("address", AddressWeight, new Query($"{filter} @title|text:({phrases})"), found));
        }
        var fused = Fuse(rankings);
        var allowed = await AllowedRecordsAsync(scope, fused.Select(f => found[f.Key]["record"].ToString()).Distinct().ToList(), cancellationToken);

        var perFault = new Dictionary<string, int>(StringComparer.Ordinal);
        return fused
            .Select(f => (Fused: f, Fields: found[f.Key]))
            .Where(x => allowed.Contains(x.Fields["record"].ToString()))
            .Where(x => NotTooManyAlike(x.Fields, perFault))
            .Take(limit)
            .Select(x => new SearchHit(
                x.Fields["record"].ToString(),
                Enum.Parse<PassageSource>(x.Fields["source"].ToString()),
                x.Fields["title"].ToString(),
                x.Fields["text"].ToString(),
                x.Fields["ids"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries),
                // Documents have no customer; the field is absent, not empty.
                int.TryParse(x.Fields["customer"].ToString(), CultureInfo.InvariantCulture, out var customer) ? customer : null,
                Math.Round(x.Fused.Score, 5),
                x.Fused.RankIn("vector"),
                x.Fused.RankIn("keyword"),
                x.Fused.RankIn("identifier") ?? x.Fused.RankIn("address")))
            .ToList();
    }

    // ------------------------------------------------------------------ the three rankings

    private async Task<Ranking> VectorRankingAsync(string question, string filter, Dictionary<string, Document> found, CancellationToken cancellationToken)
    {
        var vector = await embedder.GenerateVectorAsync(options.Value.EmbeddingQueryPrefix + question, cancellationToken: cancellationToken);
        var query = new Query($"({(filter.Length == 0 ? "*" : filter)})=>[KNN {CandidatesPerRanking} @vector $vector EF_RUNTIME {VectorSearchBreadth} AS distance]")
            .AddParam("vector", MemoryMarshal.AsBytes(vector.Span).ToArray())
            .SetSortBy("distance");
        return await RunAsync("vector", VectorWeight, query, found);
    }

    /// <summary>
    /// Passages tagged with the question's identifiers, most identifiers matched first. Ties (a
    /// model family tags hundreds of jobs) are broken by how well the rest of the question's words
    /// match, using the optional <c>~</c> clause, which scores without filtering.
    /// </summary>
    private async Task<Ranking> IdentifierRankingAsync(IReadOnlyList<string> identifiers, IReadOnlyList<string> words, string filter, Dictionary<string, Document> found)
    {
        var tags = string.Join('|', identifiers.Select(EscapeTag));
        var optional = words.Count > 0 ? $" ~@title|text:({string.Join('|', words)})" : "";
        var query = new Query($"{filter} @ids:{{{tags}}}{optional}");

        var (keys, total) = await RunQueryAsync(query, found);
        var ordered = keys
            .Select((key, index) => (key, index, matched: found[key]["ids"].ToString().Split(',').Count(identifiers.Contains)))
            .OrderByDescending(x => x.matched)
            .ThenBy(x => x.index)
            .Select(x => x.key)
            .ToList();

        var weight = total <= RareIdentifierMatches ? RareIdentifierWeight : CommonIdentifierWeight;
        return new Ranking("identifier", weight, ordered);
    }

    private async Task<Ranking> RunAsync(string name, double weight, Query query, Dictionary<string, Document> found) =>
        new(name, weight, (await RunQueryAsync(query, found)).Keys);

    private async Task<(List<string> Keys, long Total)> RunQueryAsync(Query query, Dictionary<string, Document> found)
    {
        query.ReturnFields(ReturnedFields).Limit(0, CandidatesPerRanking).Dialect(2);
        var result = await redis.GetDatabase().FT().SearchAsync(PassageIndex.Name, query);
        foreach (var document in result.Documents)
        {
            found.TryAdd(document.Id, document);
        }

        return (result.Documents.Select(d => d.Id).ToList(), result.TotalResults);
    }

    // ------------------------------------------------------------------ fusion

    internal sealed record Ranking(string Name, double Weight, IReadOnlyList<string> Keys);

    internal sealed record Fused(string Key, double Score, IReadOnlyDictionary<string, int> Ranks)
    {
        public int? RankIn(string ranking) => Ranks.TryGetValue(ranking, out var rank) ? rank : null;
    }

    /// <summary>Weighted Reciprocal Rank Fusion, best first. Ties go to the earlier ranking's order.</summary>
    internal static List<Fused> Fuse(IReadOnlyList<Ranking> rankings)
    {
        var scores = new Dictionary<string, (double Score, Dictionary<string, int> Ranks, int FirstSeen)>(StringComparer.Ordinal);
        var seen = 0;
        foreach (var ranking in rankings)
        {
            for (var i = 0; i < ranking.Keys.Count; i++)
            {
                var key = ranking.Keys[i];
                if (!scores.TryGetValue(key, out var entry))
                {
                    entry = (0, new Dictionary<string, int>(StringComparer.Ordinal), seen++);
                }

                entry.Ranks[ranking.Name] = i + 1;
                scores[key] = (entry.Score + ranking.Weight / (RrfK + i + 1), entry.Ranks, entry.FirstSeen);
            }
        }

        return scores
            .OrderByDescending(s => s.Value.Score)
            .ThenBy(s => s.Value.FirstSeen)
            .Select(s => new Fused(s.Key, s.Value.Score, s.Value.Ranks))
            .ToList();
    }

    // ------------------------------------------------------------------ near-duplicates

    /// <summary>How many jobs with the same fault and notes a result list may hold.</summary>
    public const int MaxAlikeJobs = 2;

    /// <summary>
    /// Repeat jobs for the same fault say the same thing ("E-12 code, scale on the heat
    /// exchanger"). Six copies would fill the results and push out the manual section that says
    /// how to fix it, so only the best <see cref="MaxAlikeJobs"/> of each kind are kept. The
    /// others aren't lost: they're still in the index, and counting them is a reporting question.
    /// </summary>
    private static bool NotTooManyAlike(Document fields, Dictionary<string, int> perFault)
    {
        if (fields["source"].ToString() != nameof(PassageSource.Job))
        {
            return true;
        }

        var signature = FaultSignature(fields["text"].ToString());
        perFault[signature] = perFault.GetValueOrDefault(signature) + 1;
        return perFault[signature] <= MaxAlikeJobs;
    }

    /// <summary>
    /// A job passage without the parts that make every job unique: the header (number, customer,
    /// address, date) and who wrote each note when. What's left is the fault and the fix.
    /// </summary>
    internal static string FaultSignature(string jobText)
    {
        var body = jobText.IndexOf("\n\n", StringComparison.Ordinal) is var start and >= 0 ? jobText[(start + 2)..] : jobText;
        return Whitespace().Replace(NoteByline().Replace(body, ""), " ").Trim().ToLowerInvariant();
    }

    [GeneratedRegex(@"Note from [^:\n]+:")]
    private static partial Regex NoteByline();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // ------------------------------------------------------------------ access

    /// <summary>
    /// The coarse filter Redis applies: office staff and technicians see their branch's passages
    /// and company-wide documents; the owner sees everything (an empty filter). The precise check is <see cref="AllowedRecordsAsync"/>.
    /// </summary>
    private static string BranchFilter(AccessScope scope) =>
        scope.IsOwner ? "" : $"@branch:{{{scope.BranchId?.ToString(CultureInfo.InvariantCulture) ?? "none"}|all}}";

    /// <summary>
    /// The records among the candidates that <paramref name="scope"/> may open, asked of MySQL
    /// through the same scoped queries the screens use. One query per kind of record.
    /// </summary>
    private async Task<HashSet<string>> AllowedRecordsAsync(AccessScope scope, IReadOnlyList<string> records, CancellationToken cancellationToken)
    {
        var ids = records
            .Select(r => r.Split(':'))
            .Where(parts => parts.Length == 2 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out _))
            .GroupBy(parts => parts[0], parts => int.Parse(parts[1], CultureInfo.InvariantCulture))
            .ToDictionary(g => g.Key, g => g.ToList());

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        if (ids.TryGetValue("document", out var documents))
        {
            allowed.UnionWith((await scope.Documents(db).Where(d => documents.Contains(d.Id)).Select(d => d.Id).ToListAsync(cancellationToken)).Select(id => $"document:{id}"));
        }

        if (ids.TryGetValue("equipment", out var equipment))
        {
            allowed.UnionWith((await scope.Equipment(db).Where(e => equipment.Contains(e.Id)).Select(e => e.Id).ToListAsync(cancellationToken)).Select(id => $"equipment:{id}"));
        }

        if (ids.TryGetValue("job", out var jobs))
        {
            allowed.UnionWith((await scope.Jobs(db).Where(j => jobs.Contains(j.Id)).Select(j => j.Id).ToListAsync(cancellationToken)).Select(id => $"job:{id}"));
        }

        return allowed;
    }

    // ------------------------------------------------------------------ reading the question

    /// <summary>The question's words for BM25: lower case, without question words, letters and digits only.</summary>
    internal static IReadOnlyList<string> KeywordTerms(string question) =>
        WordPattern().Matches(question.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => w.Length > 1 && !QuestionWords.Contains(w))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Identifiers in the question, upper-cased: people type <c>j-24051</c> as often as <c>J-24051</c>.
    /// A lower-case match only counts if it has a digit, so "tune-up" and "after-hours" aren't
    /// mistaken for part numbers.
    /// </summary>
    internal static IReadOnlyList<string> IdentifiersIn(string question) =>
        LooseIdentifierPattern().Matches(question)
            .Select(m => m.Value)
            .Where(id => id.Any(char.IsAsciiDigit) || id == id.ToUpperInvariant())
            .Select(id => id.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string EscapeTag(string value)
    {
        var escaped = new StringBuilder(value.Length * 2);
        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c))
            {
                escaped.Append('\\');
            }

            escaped.Append(c);
        }

        return escaped.ToString();
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();

    // The same shape as PassageBuilder's identifier pattern, but case-insensitive.
    [GeneratedRegex(@"\b[A-Z]{1,4}\d*(?:-[A-Z0-9]+)+\b", RegexOptions.IgnoreCase)]
    private static partial Regex LooseIdentifierPattern();

    /// <summary>Street addresses in the question, lower-cased for a phrase query: "14 maple st".</summary>
    internal static IReadOnlyList<string> AddressesIn(string question) =>
        AddressPattern().Matches(question).Select(m => m.Value.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();

    // A house number, one or two words, and a street type, as the seed and most Ontario
    // addresses are written ("14 Maple St", "231 Lakeview Dr", "88 Birch Cres").
    [GeneratedRegex(@"\b\d{1,5}\s+[A-Za-z]+(?:\s+[A-Za-z]+)?\s+(?:St|Street|Rd|Road|Dr|Drive|Cres|Crescent|Ave|Avenue|Pl|Place|Way|Crt|Ct|Court|Blvd|Ln|Lane)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AddressPattern();
}
