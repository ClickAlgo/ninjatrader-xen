using NinjaTrader_Xen.Services;

namespace NinjaTrader_Xen.Tests;

public sealed class NinjaTraderKnowledgeRetrieverTests
{
    [Fact]
    public void BuildQueries_ExtractsCompoundIndicatorNamesAndKeepsOriginalFallback()
    {
        const string prompt = "Build an Aroon and EMA strategy";

        var queries = NinjaTraderKnowledgeRetriever.BuildQueries(prompt);

        Assert.Equal(["Aroon", "EMA", prompt], queries);
    }

    [Fact]
    public void BuildQueries_RemovesFencedSourceCodeFromEveryQuery()
    {
        const string prompt = "Repair an Aroon strategy ```csharp\nprivate EMA ema;\n``` and EMA entries";

        var queries = NinjaTraderKnowledgeRetriever.BuildQueries(prompt);

        Assert.All(queries, query =>
        {
            Assert.DoesNotContain("private EMA", query);
            Assert.DoesNotContain("```", query);
        });
    }

    [Fact]
    public void MergeMatches_DeduplicatesByIdAndRetainsHighestSimilarity()
    {
        RagMatch[] matches =
        [
            new(1, "Aroon", "", "older", 0.61),
            new(2, "EMA", "", "ema", 0.82),
            new(1, "Aroon", "", "best", 0.91),
            new(3, "ATR", "", "atr", 0.72)
        ];

        var merged = NinjaTraderKnowledgeRetriever.MergeMatches(matches, 3);

        Assert.Equal([1, 2, 3], merged.Select(match => match.Id));
        Assert.Equal(0.91, merged[0].Similarity);
        Assert.Equal("best", merged[0].Content);
    }

    [Fact]
    public void SelectDiversifiedMatches_ReservesMatchesForEachFocusedConcept()
    {
        var aroon = new RagMatch(1, "Aroon", "", "aroon", 0.91);
        var sma = new RagMatch(2, "SMA", "", "sma", 0.88);
        var strategyOne = new RagMatch(3, "Strategy one", "", "", 0.87);
        var strategyTwo = new RagMatch(4, "Strategy two", "", "", 0.86);
        var fallback = new RagMatch(5, "Fallback strategy", "", "", 0.97);
        IReadOnlyList<IReadOnlyList<RagMatch>> focused =
        [
            [aroon, strategyOne],
            [sma, strategyTwo]
        ];

        var selected = NinjaTraderKnowledgeRetriever.SelectDiversifiedMatches(
            focused,
            [fallback, strategyOne, strategyTwo, sma, aroon],
            3,
            0.5);

        Assert.Equal([5, 1, 2], selected.Select(match => match.Id));
        Assert.Contains(selected, match => match.Title == "Aroon");
        Assert.Contains(selected, match => match.Title == "SMA");
    }

    [Fact]
    public void BuildReferenceContext_SharesTheTotalBudgetAcrossMatches()
    {
        IReadOnlyList<RagMatch> matches =
        [
            new(1, "Aroon", "Aroon reference", new string('A', 10_000), 0.91),
            new(2, "SMA", "SMA reference", new string('S', 10_000), 0.88),
            new(3, "Strategy", "Strategy reference", new string('T', 10_000), 0.85)
        ];

        var context = NinjaTraderKnowledgeRetriever.BuildReferenceContext(
            matches,
            12_000);

        Assert.True(context.Length <= 12_000);
        Assert.Contains("[Reference: Aroon]", context);
        Assert.Contains("[Reference: SMA]", context);
        Assert.Contains("[Reference: Strategy]", context);
        Assert.Contains(new string('A', 100), context);
        Assert.Contains(new string('S', 100), context);
        Assert.Contains(new string('T', 100), context);
    }

    [Fact]
    public void SelectDiversifiedMatches_ReservesStrategyAndIndicatorConcepts()
    {
        var aroon = new RagMatch(
            1, "Aroon", "", "", 0.81, "Indicator");
        var sma = new RagMatch(
            2, "SMA", "", "", 0.79, "Indicator");
        var strategy = new RagMatch(
            3, "SampleMACrossOver", "", "", 0.72, "Strategy");

        var selected = NinjaTraderKnowledgeRetriever.SelectDiversifiedMatches(
            [[aroon], [sma]],
            [aroon, sma, strategy],
            3,
            0.5,
            "Strategy");

        Assert.Equal(3, selected.Count);
        Assert.Contains(selected, match => match.Category == "Strategy");
        Assert.Contains(selected, match => match.Title == "Aroon");
        Assert.Contains(selected, match => match.Title == "SMA");
    }

    [Fact]
    public void SelectDiversifiedMatches_UsesCanonicalStrategyAsFallback()
    {
        var indicator = new RagMatch(
            1, "Aroon", "", "", 0.81, "Indicator");
        var canonical = new RagMatch(
            2, "Sample MA Cross Over", "", "", 0.34, "Strategy");

        var selected = NinjaTraderKnowledgeRetriever.SelectDiversifiedMatches(
            [[indicator]],
            [indicator, canonical],
            3,
            0.5,
            "Strategy");

        var selectedStrategy = Assert.Single(
            selected,
            match => match.Category == "Strategy");
        Assert.True(selectedStrategy.ForceInclude);
    }
}
