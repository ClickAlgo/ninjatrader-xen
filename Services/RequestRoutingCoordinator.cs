namespace NinjaTrader_Xen.Services;

public sealed record RequestRoutingContext(
    string Task,
    string Prompt,
    string? RetrievalPrompt,
    bool PromptBuilderBypassed,
    bool PromptReviewCompleted,
    bool HasCurrentCode,
    string? PreviousAssistantResponse);

public sealed record RequestRoutingOutcome(
    RequestRouteDecision Decision,
    bool IsBuildPlanStep);

public sealed class RequestRoutingCoordinator(RequestRouterService router)
{
    public async Task<RequestRoutingOutcome> DecideAsync(
        RequestRoutingContext context,
        CancellationToken cancellationToken)
    {
        var isBuildPlanStep = IsBuildPlanStep(
            context.Prompt, context.RetrievalPrompt);

        RequestRouteDecision decision;
        if (context.Task.Equals("analyse-backtest", StringComparison.OrdinalIgnoreCase))
            decision = new("analysis", false, false, false, "Analysis task.");
        else if (IsGeneratedRepair(context.Prompt))
            decision = CreateGeneratedRepairDecision();
        else if (isBuildPlanStep)
            decision = new("modify", true, true, false, "Active Build Plan step.");
        else if (context.PromptBuilderBypassed)
            decision = CreatePromptBuilderBypassDecision(context.Task);
        else
            decision = await router.RouteAsync(
                context.Task,
                context.Prompt.Trim(),
                context.HasCurrentCode,
                context.PreviousAssistantResponse,
                cancellationToken);

        if (context.PromptReviewCompleted && decision.RecommendPromptBuilder)
            decision = AcceptCompletedPromptReview(decision);

        return new(decision, isBuildPlanStep);
    }

    internal static bool IsBuildPlanStep(string prompt, string? retrievalPrompt) =>
        !string.IsNullOrWhiteSpace(retrievalPrompt) &&
        !retrievalPrompt.Trim().Equals(prompt.Trim(), StringComparison.Ordinal);

    internal static bool IsGeneratedRepair(string prompt) =>
        prompt.TrimStart().StartsWith(
            "Repair the latest complete NinjaScript source",
            StringComparison.OrdinalIgnoreCase);

    internal static RequestRouteDecision CreateGeneratedRepairDecision() =>
        new("repair", false, false, false,
            "Generated repair uses current source and diagnostics only.");

    internal static RequestRouteDecision CreatePromptBuilderBypassDecision(string task)
    {
        var intent = task.StartsWith("convert-", StringComparison.OrdinalIgnoreCase)
            ? "convert"
            : task.StartsWith("existing-", StringComparison.OrdinalIgnoreCase)
                ? "modify"
                : "build";
        return new(intent, true, true, false,
            "The user chose to build without Prompt Builder.");
    }

    internal static RequestRouteDecision AcceptCompletedPromptReview(
        RequestRouteDecision decision) =>
        RequestRouterService.ApplyIntentPolicy(
            decision.Intent,
            false,
            "Prompt review completed before submission.");
}
