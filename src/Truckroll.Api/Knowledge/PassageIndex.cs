using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using NRedisStack;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using NRedisStack.Search.Literals.Enums;
using StackExchange.Redis;
using Truckroll.Api.Ai;

namespace Truckroll.Api.Knowledge;

/// <summary>
/// The Redis side of retrieval: one Query Engine index over every passage, searchable three ways
/// at once (docs/adr/003).
/// </summary>
/// <remarks>
/// <para>Each passage is a hash under <c>passage:{record}:{n}</c> with these fields:</para>
/// <list type="bullet">
/// <item><c>text</c> and <c>title</c>: full-text fields, ranked by BM25. Title words count double.</item>
/// <item><c>ids</c>: a tag field of exact identifiers (job, model, serial, part numbers), which a
/// vector search smears and BM25 tokenises apart.</item>
/// <item><c>source</c>, <c>record</c>, <c>branch</c>, <c>customer</c>: tags for filtering.</item>
/// <item><c>vector</c>: the embedding, in an HNSW graph with cosine distance.</item>
/// </list>
/// <para>
/// A set per record (<c>passages:keys:{record}</c>) lists its passage keys, so re-indexing a record
/// deletes exactly its old passages, in the same transaction that writes the new ones. A job that
/// shrinks from three passages to two leaves nothing stale behind, and a search never sees a job
/// half-replaced.
/// </para>
/// </remarks>
public sealed class PassageIndex(IConnectionMultiplexer redis, IOptions<AiOptions> options)
{
    public const string Name = "passages";
    public const string KeyPrefix = "passage:";
    private const string RecordKeysPrefix = "passages:keys:";
    private const string MetaKey = "passages:meta";

    /// <summary>
    /// Bump this when the schema or the passage format changes. Startup compares it with the
    /// index's metadata and rebuilds on a mismatch, so nobody has to remember to.
    /// </summary>
    public const int SchemaVersion = 1;

    private IDatabase Database => redis.GetDatabase();

    /// <summary>
    /// True when the index exists, finished building, and was built with today's schema version,
    /// embedding model and vector width. Anything else needs a rebuild.
    /// </summary>
    public async Task<bool> IsCurrentAsync()
    {
        if (!await ExistsAsync())
        {
            return false;
        }

        var meta = (await Database.HashGetAllAsync(MetaKey)).ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        var ai = options.Value;
        return meta.GetValueOrDefault("schema") == SchemaVersion.ToString(CultureInfo.InvariantCulture)
            && meta.GetValueOrDefault("model") == ai.EmbeddingModel
            && meta.GetValueOrDefault("dimensions") == ai.EmbeddingDimensions.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Drops the index and every passage, then creates it empty.</summary>
    public async Task RecreateAsync()
    {
        var search = Database.FT();
        if (await ExistsAsync())
        {
            // DD: delete the passage hashes too, not just the index over them.
            await search.DropIndexAsync(Name, dd: true);
        }

        await Database.KeyDeleteAsync(MetaKey);
        foreach (var server in redis.GetServers().Where(s => !s.IsReplica))
        {
            var keys = new List<RedisKey>();
            await foreach (var key in server.KeysAsync(Database.Database, RecordKeysPrefix + "*"))
            {
                keys.Add(key);
            }

            if (keys.Count > 0)
            {
                await Database.KeyDeleteAsync([.. keys]);
            }
        }

        var schema = new Schema()
            .AddTextField("title", weight: 2.0)
            .AddTextField("text")
            .AddTagField("ids")
            .AddTagField("source")
            .AddTagField("record")
            .AddTagField("branch")
            .AddTagField("customer")
            .AddVectorField("vector", Schema.VectorField.VectorAlgo.HNSW, new Dictionary<string, object>
            {
                ["TYPE"] = "FLOAT32",
                ["DIM"] = options.Value.EmbeddingDimensions,
                ["DISTANCE_METRIC"] = "COSINE",
            });

        await search.CreateAsync(Name, new FTCreateParams().On(IndexDataType.HASH).Prefix(KeyPrefix), schema);
    }

    /// <summary>Records that a full build finished, with what it was built from.</summary>
    public Task MarkBuiltAsync(IndexReport report) =>
        Database.HashSetAsync(MetaKey,
        [
            new("schema", SchemaVersion),
            new("model", options.Value.EmbeddingModel),
            new("dimensions", options.Value.EmbeddingDimensions),
            new("builtAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            new("passages", report.Passages),
            new("seconds", report.Seconds),
        ]);

    /// <summary>
    /// Replaces every passage of one record. An empty list removes the record from the index
    /// (a deleted job, or one with nothing left to say).
    /// </summary>
    public async Task ReplaceAsync(string record, IReadOnlyList<(Passage Passage, float[] Vector)> passages)
    {
        var setKey = (RedisKey)(RecordKeysPrefix + record);
        var oldKeys = await Database.SetMembersAsync(setKey);

        var transaction = Database.CreateTransaction();
        var writes = new List<Task>();
        foreach (var key in oldKeys)
        {
            writes.Add(transaction.KeyDeleteAsync(key.ToString()));
        }

        writes.Add(transaction.KeyDeleteAsync(setKey));
        for (var i = 0; i < passages.Count; i++)
        {
            var key = $"{KeyPrefix}{record}:{i}";
            writes.Add(transaction.HashSetAsync(key, Fields(passages[i].Passage, passages[i].Vector)));
            writes.Add(transaction.SetAddAsync(setKey, key));
        }

        await transaction.ExecuteAsync();
        await Task.WhenAll(writes);
    }

    /// <summary>How many passages the index holds.</summary>
    public async Task<long> CountAsync() =>
        await ExistsAsync() ? (await Database.FT().InfoAsync(Name)).NumDocs : 0;

    /// <summary>The stored fields of one passage, for tests and diagnostics.</summary>
    public async Task<IReadOnlyList<Dictionary<string, string>>> PassagesOfAsync(string record)
    {
        var keys = (await Database.SetMembersAsync(RecordKeysPrefix + record)).Select(k => k.ToString()).Order(StringComparer.Ordinal);
        var result = new List<Dictionary<string, string>>();
        foreach (var key in keys)
        {
            var fields = await Database.HashGetAllAsync(key);
            result.Add(fields.Where(f => f.Name != "vector").ToDictionary(f => f.Name.ToString(), f => f.Value.ToString()));
        }

        return result;
    }

    private async Task<bool> ExistsAsync() =>
        (await Database.FT()._ListAsync()).Any(name => name.ToString() == Name);

    private static HashEntry[] Fields(Passage passage, float[] vector)
    {
        var fields = new List<HashEntry>
        {
            new("title", passage.Title),
            new("text", passage.Text),
            new("source", passage.Source.ToString()),
            new("record", passage.Record),
            // Company-wide documents are tagged "all", so a branch filter is "my branch OR all".
            new("branch", passage.BranchId?.ToString(CultureInfo.InvariantCulture) ?? "all"),
            new("ids", string.Join(',', passage.Identifiers)),
            // FLOAT32 little-endian, the layout the vector field expects.
            new("vector", MemoryMarshal.AsBytes(vector.AsSpan()).ToArray()),
        };

        if (passage.CustomerId is { } customer)
        {
            fields.Add(new("customer", customer.ToString(CultureInfo.InvariantCulture)));
        }

        return [.. fields];
    }
}

/// <summary>What a full build did, and how long it took.</summary>
public sealed record IndexReport(int Documents, int Equipment, int Jobs, int Passages, double Seconds);
