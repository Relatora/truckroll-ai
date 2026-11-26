using System.Numerics.Tensors;
using Truckroll.Api.Tests.Infrastructure;

namespace Truckroll.Api.Tests.Ai;

/// <summary>The fake embedder has to behave enough like a real one for retrieval tests to mean something.</summary>
public sealed class FakeEmbeddingGeneratorTests
{
    private readonly FakeEmbeddingGenerator _embedder = new(768);

    [Fact]
    public void The_same_text_always_gets_the_same_vector()
    {
        Assert.Equal(_embedder.Embed("Carrier ignitor failure"), _embedder.Embed("carrier IGNITOR failure!"));
    }

    [Fact]
    public void Vectors_have_the_configured_width_and_unit_length()
    {
        var vector = _embedder.Embed("Annual furnace tune-up at 14 Maple St");

        Assert.Equal(768, vector.Length);
        Assert.Equal(1f, MathF.Sqrt(TensorPrimitives.Dot(vector, vector)), precision: 4);
    }

    [Fact]
    public void Texts_that_share_words_are_closer_than_texts_that_do_not()
    {
        var question = _embedder.Embed("Why does the Carrier furnace ignitor keep failing?");
        var related = _embedder.Embed("Carrier furnace ignitor failed again, replaced ignitor");
        var unrelated = _embedder.Embed("Water heater anode rod replacement");

        Assert.True(
            TensorPrimitives.CosineSimilarity(question, related) > TensorPrimitives.CosineSimilarity(question, unrelated) + 0.2f,
            "Shared words should pull vectors together.");
    }
}
