using Truckroll.Api.Knowledge;
using static Truckroll.Api.Knowledge.KnowledgeSearch;

namespace Truckroll.Api.Tests.Knowledge;

/// <summary>Weighted Reciprocal Rank Fusion, and how a question is read before it's searched.</summary>
public sealed class FusionTests
{
    [Fact]
    public void A_passage_found_by_two_rankings_beats_one_found_first_by_only_one()
    {
        var fused = Fuse(
        [
            new Ranking("vector", VectorWeight, ["a", "b", "c"]),
            new Ranking("keyword", KeywordWeight, ["c", "d"]),
        ]);

        // c: 1/63 + 1/61 beats a: 1/61.
        Assert.Equal(["c", "a", "b", "d"], fused.Select(f => f.Key));
        Assert.Equal(3, fused[0].RankIn("vector"));
        Assert.Equal(1, fused[0].RankIn("keyword"));
        Assert.Null(fused[1].RankIn("keyword"));
    }

    [Fact]
    public void A_rare_identifier_match_outvotes_both_other_rankings_agreeing()
    {
        var fused = Fuse(
        [
            new Ranking("vector", VectorWeight, ["similar", "exact"]),
            new Ranking("keyword", KeywordWeight, ["similar"]),
            new Ranking("identifier", RareIdentifierWeight, ["exact"]),
        ]);

        // exact: 1/62 + 3/61 beats similar: 1/61 + 1/61.
        Assert.Equal("exact", fused[0].Key);
    }

    [Fact]
    public void Equal_scores_keep_the_order_the_first_ranking_found_them_in()
    {
        var fused = Fuse([new Ranking("vector", 1, ["x"]), new Ranking("keyword", 1, ["y"])]);

        Assert.Equal(["x", "y"], fused.Select(f => f.Key));
    }

    [Fact]
    public void Two_jobs_for_the_same_fault_share_a_signature_whoever_and_wherever_they_were()
    {
        const string first = "Repair job J-21517 for Ana Lima at 46 Willow Dr, Kitchener on November 28, 2025, completed.\n\n" +
            "Reported: Tankless showing E-12\n\nNote from Marco Bellini, November 28, 2025: E-12 code, scale on the heat exchanger.";
        const string second = "Repair job J-21936 for Raj Patel at 221 Juniper Pl, Cambridge on September 8, 2026, completed.\n\n" +
            "Reported: Tankless showing E-12\n\nNote from Paul Vandermeer, September 8, 2026: E-12 code, scale on the heat exchanger.";
        const string different = "Repair job J-21000 for Raj Patel at 221 Juniper Pl, Cambridge on September 8, 2026, completed.\n\n" +
            "Reported: Pilot keeps going out\n\nNote from Paul Vandermeer, September 8, 2026: Thermocouple output 8 mV.";

        Assert.Equal(FaultSignature(first), FaultSignature(second));
        Assert.NotEqual(FaultSignature(first), FaultSignature(different));
    }

    [Fact]
    public void Keyword_terms_drop_question_words_and_punctuation()
    {
        Assert.Equal(["furnace", "keep", "failing", "maple", "st"], KeywordTerms("Why does the furnace keep failing at Maple St?"));
    }

    [Theory]
    [InlineData("What did we install at 14 Maple St?", "14 maple st")]
    [InlineData("boiler at 88 birch cres and 231 Lakeview Drive", "88 birch cres,231 lakeview drive")]
    [InlineData("Which furnaces keep failing in 2026?", "")]
    public void Street_addresses_are_found_for_an_exact_phrase_match(string question, string expected)
    {
        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries), AddressesIn(question));
    }

    [Theory]
    [InlineData("status of j-24051?", "J-24051")]
    [InlineData("Is CAL80-20-04417 under bulletin SB-2024-07", "CAL80-20-04417,SB-2024-07")]
    [InlineData("what does W-KESTREL cover", "W-KESTREL")]
    [InlineData("do members get a tune-up after-hours", "")]
    public void Identifiers_are_found_in_any_case_but_hyphenated_words_are_not_identifiers(string question, string expected)
    {
        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries), IdentifiersIn(question));
    }
}
