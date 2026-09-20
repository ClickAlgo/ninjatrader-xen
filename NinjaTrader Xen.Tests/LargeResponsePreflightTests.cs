using NinjaTrader_Xen.Endpoints;

namespace NinjaTrader_Xen.Tests;

public sealed class LargeResponsePreflightTests
{
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
}
