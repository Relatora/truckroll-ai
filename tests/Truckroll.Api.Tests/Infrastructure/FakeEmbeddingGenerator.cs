using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Truckroll.Api.Tests.Infrastructure;

/// <summary>
/// A deterministic stand-in for the embedding model. Each word is hashed to one of the vector's
/// positions (the "hashing trick"), and the vector is scaled to length 1.
/// </summary>
/// <remarks>
/// It understands nothing, but it keeps the one property retrieval tests need: texts that share
/// words get similar vectors, and the same text always gets the same vector. "Ignitor failure on
/// a Carrier furnace" lands near "Carrier ignitor failed" and far from "water heater anode rod".
/// </remarks>
public sealed partial class FakeEmbeddingGenerator(int dimensions) : IEmbeddingGenerator<string, Embedding<float>>
{
    public const string ModelId = "fake-embed";

    private readonly EmbeddingGeneratorMetadata _metadata = new("fake", defaultModelId: ModelId, defaultModelDimensions: dimensions);

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
            values.Select(value => new Embedding<float>(Embed(value)) { ModelId = ModelId })));

    /// <summary>The vector for one text.</summary>
    public float[] Embed(string text)
    {
        var vector = new float[dimensions];
        foreach (Match word in Words().Matches(text.ToLowerInvariant()))
        {
            var hash = Fnv1a(word.Value);
            // The low bits pick the position, a high bit picks the sign, so unrelated words that
            // collide on a position tend to cancel out rather than pile up.
            var sign = (hash & 0x8000_0000) == 0 ? 1f : -1f;
            vector[hash % (uint)dimensions] += sign;
        }

        var length = MathF.Sqrt(vector.Sum(x => x * x));
        if (length > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= length;
            }
        }

        return vector;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(EmbeddingGeneratorMetadata) ? _metadata
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose()
    {
    }

    // FNV-1a: tiny, stable across runs and machines (string.GetHashCode is randomised per process).
    private static uint Fnv1a(string text)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            hash = (hash ^ b) * 16777619u;
        }

        return hash;
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Words();
}
