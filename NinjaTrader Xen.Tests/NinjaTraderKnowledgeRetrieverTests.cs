using NinjaTrader_Xen.Services;

namespace NinjaTrader_Xen.Tests;

public sealed class NinjaTraderKnowledgeRetrieverTests
{
    [Fact]
    public void RagRecordEmbeddingText_UsesOnlyTitleDescriptionAndTags()
    {
        var embeddingText = RagRecordEmbeddingText.Build(
            "Add a Custom Button to a Chart",
            "Adds a button using ChartControl.Dispatcher.",
            "chart button, WPF, ChartControl");

        Assert.Equal(
            "Title: Add a Custom Button to a Chart\n" +
            "Description: Adds a button using ChartControl.Dispatcher.\n" +
            "Tags: chart button, WPF, ChartControl",
            embeddingText);
        Assert.DoesNotContain("public class", embeddingText);
        Assert.DoesNotContain("CustomButton.cs", embeddingText);
        Assert.DoesNotContain("Indicator", embeddingText);
    }

    [Fact]
    public void BuildQueries_ExtractsCompoundIndicatorNamesAndKeepsOriginalFallback()
    {
        const string prompt = "Build an Aroon and EMA strategy";

        var queries = NinjaTraderKnowledgeRetriever.BuildQueries(prompt);

        Assert.Equal(["Aroon", "EMA", prompt], queries);
    }

    [Fact]
    public void BuildQueries_SeparatesRsiAndBreakoutAndKeepsOriginalFallback()
    {
        const string prompt = "build an rsi and break out strategy";

        var queries = NinjaTraderKnowledgeRetriever.BuildQueries(prompt);

        Assert.Equal(["rsi", "break out", prompt], queries);
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

        Assert.Equal([1, 2], selected.Select(match => match.Id));
        Assert.Contains(selected, match => match.Title == "Aroon");
        Assert.Contains(selected, match => match.Title == "SMA");
    }

    [Fact]
    public void SelectDiversifiedMatches_SimpleStrategyDoesNotAddFiller()
    {
        var primary = new RagMatch(1, "Break-even strategy", "", "", 0.82, "Strategy");
        var unrelatedStrategy = new RagMatch(2, "Unrelated strategy", "", "", 0.79, "Strategy");
        var unrelatedIndicator = new RagMatch(3, "Unrelated indicator", "", "", 0.78, "Indicator");

        var selected = NinjaTraderKnowledgeRetriever.SelectDiversifiedMatches(
            [], [primary, unrelatedStrategy, unrelatedIndicator], 3, 0.5, "Strategy");

        Assert.Equal([primary], selected);
    }

    [Fact]
    public void SelectDiversifiedMatches_UsesOriginalQueryForPrimaryAndIndicatorsForFocusedSlots()
    {
        var fullQueryPrimary = new RagMatch(1, "Break-even strategy", "", "", 0.71, "Strategy");
        var focusedStrategy = new RagMatch(2, "RSI strategy", "", "", 0.96, "Strategy");
        var rsi = new RagMatch(3, "RSI indicator", "", "", 0.91, "Indicator");

        var selected = NinjaTraderKnowledgeRetriever.SelectDiversifiedMatches(
            [[focusedStrategy, rsi]],
            [fullQueryPrimary, rsi],
            3,
            0.5,
            "Strategy");

        Assert.Equal([fullQueryPrimary, rsi], selected);
    }

    [Fact]
    public void SelectDiversifiedMatches_PrefersFocusedStrategyConceptUsingOriginalScore()
    {
        var genericPrimary = new RagMatch(
            1, "Use Indicator Signals Inside a Strategy", "", "", 0.4587, "Strategy");
        var breakout = new RagMatch(
            2, "Trade a Breakout Above the Initial Session High", "", "", 0.4162, "Strategy");
        var rsi = new RagMatch(
            3, "Relative Strength Index RSI Oscillator", "", "", 0.4651, "Indicator");

        var selected = NinjaTraderKnowledgeRetriever.SelectDiversifiedMatches(
            [[rsi], [breakout]],
            [rsi, genericPrimary, breakout],
            3,
            0.4,
            "Strategy",
            new HashSet<int> { breakout.Id });

        Assert.Equal([breakout, rsi], selected);
    }

    [Fact]
    public void SelectDiversifiedMatches_DoesNotPreferFocusedStrategyBelowOriginalThreshold()
    {
        var genericPrimary = new RagMatch(1, "Generic strategy", "", "", 0.46, "Strategy");
        var weakBreakout = new RagMatch(2, "Weak breakout", "", "", 0.39, "Strategy");

        var selected = NinjaTraderKnowledgeRetriever.SelectDiversifiedMatches(
            [[weakBreakout]],
            [genericPrimary, weakBreakout],
            3,
            0.4,
            "Strategy",
            new HashSet<int> { weakBreakout.Id });

        Assert.Equal(genericPrimary, selected[0]);
    }

    [Fact]
    public void SelectDiversifiedMatches_DoesNotInjectIndicatorsWithoutPrimaryStrategy()
    {
        var rsi = new RagMatch(1, "RSI indicator", "", "", 0.91, "Indicator");

        var selected = NinjaTraderKnowledgeRetriever.SelectDiversifiedMatches(
            [[rsi]], [rsi], 3, 0.5, "Strategy");

        Assert.Empty(selected);
    }

    [Theory]
    [InlineData("build-strategy")]
    [InlineData("existing-strategy")]
    [InlineData("convert-strategy")]
    public void StrategyTasksShareStrategyThenIndicatorCategories(string task)
    {
        Assert.Equal(
            ["Strategy", "Indicator"],
            Endpoints.ChatEndpoints.GetRagCategories(task));
    }

    [Theory]
    [InlineData("build-indicator")]
    [InlineData("existing-indicator")]
    [InlineData("convert-indicator")]
    public void IndicatorTasksSearchOnlyIndicatorCategory(string task)
    {
        Assert.Equal(
            ["Indicator"],
            Endpoints.ChatEndpoints.GetRagCategories(task));
    }

    [Fact]
    public void BuildReferenceContext_KeepsPrimaryStrategyFirst()
    {
        IReadOnlyList<RagMatch> matches =
        [
            new(1, "Primary strategy", "", "strategy", 0.61, "Strategy"),
            new(2, "RSI indicator", "", "indicator", 0.93, "Indicator")
        ];

        var context = NinjaTraderKnowledgeRetriever.BuildReferenceContext(matches, 12_000);

        Assert.True(
            context.IndexOf("[Reference: Primary strategy]", StringComparison.Ordinal) <
            context.IndexOf("[Reference: RSI indicator]", StringComparison.Ordinal));
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
