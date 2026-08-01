using NinjaTrader_Xen.Services;

namespace NinjaTrader_Xen.Tests;

public sealed class TradingRequestComplexityPolicyTests
{
    [Fact]
    public void Evaluate_AllowsNormalDetailedStrategyRequest()
    {
        var prompt = "Build an EMA crossover strategy with configurable periods, " +
            "a fixed stop loss, profit target, session filter and one open position.";

        var result = TradingRequestComplexityPolicy.Evaluate(
            "build-strategy",
            prompt);

        Assert.False(result.Rejected);
    }

    [Fact]
    public void Evaluate_RejectsVeryLongNewBuildRequirements()
    {
        var prompt = "Build a strategy. " + new string('x', 4_500);

        var result = TradingRequestComplexityPolicy.Evaluate(
            "build-strategy",
            prompt);

        Assert.True(result.Rejected);
    }

    [Fact]
    public void Evaluate_RejectsOverloadedAdvancedBuild()
    {
        var features = "grid martingale hedging dashboard telegram news calendar ";
        var instructions = string.Join(
            "\n",
            Enumerable.Range(1, 12).Select(index =>
                $"{index}. Add and implement feature {index}."));

        var result = TradingRequestComplexityPolicy.Evaluate(
            "build-strategy",
            features + instructions);

        Assert.True(result.Rejected);
    }

    [Fact]
    public void Evaluate_AllowsLargeSourceWithOneFocusedChange()
    {
        var prompt = "Add a trailing stop.\n```csharp\n" +
            new string('c', 26_000) +
            "\n```";

        var result = TradingRequestComplexityPolicy.Evaluate(
            "existing-strategy",
            prompt);

        Assert.False(result.Rejected);
    }

    [Fact]
    public void Evaluate_RejectsManyChangesAgainstLargeSource()
    {
        var instructions = string.Join(
            "\n",
            Enumerable.Range(1, 6).Select(index =>
                $"{index}. Add feature {index}."));
        var prompt = instructions + "\n```csharp\n" +
            new string('c', 13_000) +
            "\n```";

        var result = TradingRequestComplexityPolicy.Evaluate(
            "existing-strategy",
            prompt);

        Assert.True(result.Rejected);
    }
}
