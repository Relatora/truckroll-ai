using System.Globalization;
using Truckroll.Data.Domain;

namespace Truckroll.Data.Seed;

/// <summary>
/// Reads the markdown files embedded under <c>Seed/Documents</c> and <c>Seed/Calls</c>. Each
/// starts with a small front-matter block of <c>key: value</c> lines between <c>---</c> markers.
/// </summary>
internal static class SeedResources
{
    public static IEnumerable<(Dictionary<string, string> Meta, string Body)> Read(string folder)
    {
        var assembly = typeof(SeedResources).Assembly;
        var prefix = folder + "/";

        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return Parse(reader.ReadToEnd().ReplaceLineEndings("\n"));
        }
    }

    internal static (Dictionary<string, string> Meta, string Body) Parse(string text)
    {
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!text.StartsWith("---\n", StringComparison.Ordinal))
        {
            return (meta, text);
        }

        var end = text.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        foreach (var line in text[4..end].Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            meta[line[..colon].Trim()] = line[(colon + 1)..].Trim().Trim('"');
        }

        return (meta, text[(end + 5)..].Trim() + "\n");
    }
}

/// <summary>The knowledge base: manuals, warranty terms, policies and service bulletins.</summary>
internal static class SeedDocuments
{
    public static IEnumerable<KnowledgeDocument> Load(DateOnly today, IReadOnlyDictionary<string, Branch> branches)
    {
        var updated = new DateTimeOffset(today.AddDays(-45).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        foreach (var (meta, body) in SeedResources.Read("documents"))
        {
            yield return new KnowledgeDocument
            {
                Code = meta["code"],
                Title = meta["title"],
                Kind = Enum.Parse<DocumentKind>(meta["kind"]),
                AppliesToModel = meta.GetValueOrDefault("applies_to"),
                Branch = meta.TryGetValue("branch", out var code) ? branches[code] : null,
                Body = body,
                UpdatedAt = updated,
            };
        }
    }
}

/// <summary>This morning's calls, waiting in the inbox for the assistant and a CSR (#15).</summary>
internal static class SeedCalls
{
    /// <summary>Caller numbers used by the transcripts, kept out of the generated customers' pool.</summary>
    public static IEnumerable<string> CallerPhones() =>
        SeedResources.Read("calls").Select(call => call.Meta["caller_phone"]);

    public static IEnumerable<CallRecord> Load(DateOnly today, IReadOnlyDictionary<string, Branch> branches, IReadOnlyList<Customer> customers)
    {
        foreach (var (meta, body) in SeedResources.Read("calls"))
        {
            var time = TimeOnly.Parse(meta["received"], CultureInfo.InvariantCulture);
            var local = today.ToDateTime(time);

            yield return new CallRecord
            {
                Branch = branches[meta["branch"]],
                // Left unmatched on purpose: matching the caller to a customer is part of what the
                // assistant proposes and a person confirms.
                Customer = null,
                ReceivedAt = new DateTimeOffset(local, DemoDataGenerator.LocalZone.GetUtcOffset(local)).ToUniversalTime(),
                Transcript = body,
                Status = CallStatus.New,
            };
        }
    }
}
