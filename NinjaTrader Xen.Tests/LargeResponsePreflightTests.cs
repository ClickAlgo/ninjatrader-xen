using NinjaTrader_Xen.Endpoints;

namespace NinjaTrader_Xen.Tests;

public sealed class LargeResponsePreflightTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Theory]
    [InlineData("build", true)]
    [InlineData("modify", true)]
    [InlineData("convert", true)]
    [InlineData("repair", true)]
    [InlineData("question", false)]
    [InlineData("analysis", false)]
    public void CompleteSourceGuard_AppliesOnlyToFullFileIntents(
        string intent,
        bool expected)
    {
        Assert.Equal(expected,
            ChatEndpoints.RequiresCompleteSourceResponse(intent, "class Strategy {}"));
    }

    [Fact]
    public void CompleteSourceGuard_DoesNotApplyWithoutCurrentCode()
    {
        Assert.False(ChatEndpoints.RequiresCompleteSourceResponse("modify", ""));
    }

    [Theory]
    [InlineData(20_000, 10_000, true)]
    [InlineData(40_000, 10_000, false)]
    [InlineData(20_000, 5_000, false)]
    public void CompleteSourceFit_ReservesChangeAndFramingHeadroom(
        int sourceCharacters,
        int allowance,
        bool expected)
    {
        Assert.Equal(expected,
            ChatEndpoints.CanLikelyFitCompleteSource(
                new string('x', sourceCharacters), allowance));
    }

    [Fact]
    public void AbsoluteSourceLimit_IsCheckedBeforeLowBalance()
    {
        var endpoint = File.ReadAllText(Path.Combine(
            Root, "Endpoints", "ChatEndpoints.cs"));
        var absoluteLimitCheck = endpoint.IndexOf(
            "!CanLikelyFitCompleteSource(currentCode, MaximumResponseTokens)",
            StringComparison.Ordinal);
        var lowBalanceCheck = endpoint.IndexOf(
            "if (maximumOutputTokens < 800)",
            StringComparison.Ordinal);

        Assert.True(absoluteLimitCheck >= 0);
        Assert.True(lowBalanceCheck > absoluteLimitCheck);
        Assert.Contains("even with additional credit", endpoint);
        Assert.Contains("Ask Xen to analyse it without rewriting the full file", endpoint);
        Assert.Contains(
            "https://help.clickalgo.com/ninjatrader-xen/convert-strategies/#large-strategy-files",
            endpoint);
    }

    [Fact]
    public void AbsoluteSourceLimit_HelpLinkIsRenderedForTheCustomer()
    {
        var script = File.ReadAllText(Path.Combine(
            Root, "wwwroot", "js", "workspace.js"));

        Assert.Contains("requestError.helpUrl = eventData.helpUrl", script);
        Assert.Contains("help.href = error.helpUrl", script);
        Assert.Contains("help.target = \"_blank\"", script);
        Assert.Contains("help.rel = \"noopener\"", script);
        Assert.Contains("Learn why large strategy conversions are limited", script);
    }
}
