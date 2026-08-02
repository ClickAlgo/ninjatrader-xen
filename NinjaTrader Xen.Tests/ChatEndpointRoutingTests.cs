using NinjaTrader_Xen.Endpoints;
using NinjaTrader_Xen.Services;

namespace NinjaTrader_Xen.Tests;

public sealed class ChatEndpointRoutingTests
{
    [Fact]
    public void DirectCodingRequestPreservesEveryCompoundTerm()
    {
        var result = ChatEndpoints.SelectRetrievalPrompt(
            "build a aroon and ema strategy",
            "EMA strategy",
            false);

        Assert.Equal("build a aroon and ema strategy", result);
    }

    [Fact]
    public void BuildPlanUsesOriginalRequestAndCurrentStepContext()
    {
        const string combined =
            "Build an Aroon and EMA strategy\n\nCurrent Build Plan step:\nAdd EMA entry logic";
        var result = ChatEndpoints.SelectRetrievalPrompt(
            "Add EMA entry logic",
            combined,
            true);

        Assert.Equal(combined, result);
    }

    [Fact]
    public void BuildAnywayForcesCodingRagAndDurableMemory()
    {
        var result = RequestRoutingCoordinator.CreatePromptBuilderBypassDecision(
            "build-strategy");

        Assert.Equal("build", result.Intent);
        Assert.True(result.UseRag);
        Assert.True(result.StoreAsRequirement);
        Assert.False(result.RecommendPromptBuilder);
    }

    [Fact]
    public void QuestionHistoryIsTemporaryCompactAndExcludesSourceCode()
    {
        var history = new[]
        {
            new NinjaTrader_Xen.Models.ChatTurn("user", "Build an SMA strategy"),
            new NinjaTrader_Xen.Models.ChatTurn("assistant",
                "I used 20 and 50 periods. ```csharp\npublic class PrivateCode {}\n```")
        };

        var result = ChatEndpoints.BuildEphemeralQuestionHistory(history);

        Assert.Equal(2, result.Count);
        Assert.Contains("20 and 50", result[1].Content);
        Assert.DoesNotContain("PrivateCode", result[1].Content);
    }

    [Fact]
    public void CoordinatorRecognisesBuildPlanAndGeneratedRepairFlows()
    {
        Assert.True(RequestRoutingCoordinator.IsBuildPlanStep(
            "Add EMA entry logic",
            "Original Aroon strategy\nCurrent Build Plan step: Add EMA entry logic"));
        Assert.True(RequestRoutingCoordinator.IsGeneratedRepair(
            "Repair the latest complete NinjaScript source so it passes Build Check"));
        var repair = RequestRoutingCoordinator.CreateGeneratedRepairDecision();
        Assert.False(repair.UseRag);
        Assert.False(repair.StoreAsRequirement);
    }

    [Fact]
    public void CompletedFrontendReviewCannotBeReopenedByAContextDifference()
    {
        var result = RequestRoutingCoordinator.AcceptCompletedPromptReview(
            new RequestRouteDecision(
                "build", false, false, true, "Needs planning"));

        Assert.False(result.RecommendPromptBuilder);
        Assert.True(result.UseRag);
        Assert.True(result.StoreAsRequirement);
    }
}
