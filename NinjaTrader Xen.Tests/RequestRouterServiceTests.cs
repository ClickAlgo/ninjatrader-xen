using NinjaTrader_Xen.Services;

namespace NinjaTrader_Xen.Tests;

public sealed class RequestRouterServiceTests
{
    [Fact]
    public void FirstBuildFallbackUsesRagAndStoresRequirement()
    {
        var result = RequestRouterService.CreateFallback(
            "build-strategy", false);
        Assert.True(result.UseRag);
        Assert.True(result.StoreAsRequirement);
        Assert.Equal("build", result.Intent);
    }

    [Fact]
    public void EstablishedProjectFallbackDoesNotPolluteRagOrMemory()
    {
        var result = RequestRouterService.CreateFallback(
            "build-strategy", true);
        Assert.False(result.UseRag);
        Assert.False(result.StoreAsRequirement);
    }

    [Fact]
    public void SafeSummaryRemovesSourceAndCapsLength()
    {
        var prompt = "Explain this\n```csharp\npublic class Secret { }\n```\n" +
            new string('x', 3000);
        var result = RequestRouterService.BuildSafeRequestSummary(prompt);
        Assert.DoesNotContain("Secret", result);
        Assert.True(result.Length <= 1800);
    }

    [Fact]
    public void PreviousResponseSummaryRemovesCode()
    {
        var result = RequestRouterService.SummarizePreviousResponse(
            "Overview text ```csharp\nprivate string secret;\n``` next");
        Assert.Equal("Overview text next", result);
    }

    [Theory]
    [InlineData("build")]
    [InlineData("modify")]
    [InlineData("convert")]
    public void CodingIntentsAlwaysUseRag(string intent)
    {
        var result = RequestRouterService.ApplyIntentPolicy(
            intent, false, "Coding request");
        Assert.True(result.UseRag);
    }

    [Theory]
    [InlineData("question")]
    [InlineData("analysis")]
    public void NonCodingIntentsNeverUseRagOrMemory(string intent)
    {
        var result = RequestRouterService.ApplyIntentPolicy(
            intent, false, "Non-coding");
        Assert.False(result.UseRag);
        Assert.False(result.StoreAsRequirement);
    }

    [Fact]
    public void RepairUsesDiagnosticsWithoutRagOrDurableRequirements()
    {
        var result = RequestRouterService.ApplyIntentPolicy(
            "repair", false, "Repair");
        Assert.False(result.UseRag);
        Assert.False(result.StoreAsRequirement);
    }

    [Fact]
    public void CodingRequestAwaitingClarificationDoesNotUseRagOrMemory()
    {
        var result = RequestRouterService.ApplyIntentPolicy(
            "build", true, "Needs grid rules");

        Assert.True(result.RecommendPromptBuilder);
        Assert.False(result.UseRag);
        Assert.False(result.StoreAsRequirement);
    }

    [Fact]
    public void ClarifiedCodingRequestUsesRagAndMemory()
    {
        var result = RequestRouterService.ApplyIntentPolicy(
            "build", false, "Ready");

        Assert.True(result.UseRag);
        Assert.True(result.StoreAsRequirement);
    }
}
