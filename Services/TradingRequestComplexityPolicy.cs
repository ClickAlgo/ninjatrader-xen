using System.Text.RegularExpressions;

namespace NinjaTrader_Xen.Services;

public static class TradingRequestComplexityPolicy
{
    public static RequestComplexityResult Evaluate(
        string task,
        string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return RequestComplexityResult.Allowed;

        var isNewBuild = task is "build-strategy" or "build-indicator";
        var isSourceTask = task is
            "existing-strategy" or "existing-indicator" or
            "convert-strategy" or "convert-indicator";
        if (!isNewBuild && !isSourceTask)
            return RequestComplexityResult.Allowed;

        var fencedCodeLength = Regex.Matches(
                prompt,
                "```[\\s\\S]*?```",
                RegexOptions.CultureInvariant)
            .Sum(match => match.Length);
        var requirements = Regex.Replace(
            prompt,
            "```[\\s\\S]*?```",
            " ",
            RegexOptions.CultureInvariant);
        var requirementLength = requirements.Trim().Length;
        var numberedSteps = Regex.Matches(
            requirements,
            @"(?m)^\s*\d+\s*[\.\)\-:]\s+").Count;
        var bulletPoints = Regex.Matches(
            requirements,
            @"(?m)^\s*[-*\u2022]\s+").Count;
        var changeVerbs = Regex.Matches(
            requirements,
            @"\b(add|change|modify|update|remove|replace|fix|refactor|rewrite|adjust|convert|improve|extend|move|rename|create|build|implement|include|use|make|prevent|allow)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;
        var instructionSignals = numberedSteps + bulletPoints;

        if (isNewBuild)
        {
            var advancedFeatures = Regex.Matches(
                requirements,
                @"\b(grid|martingale|hedging|dashboard|panel|toolbar|button|telegram|email|news|calendar|machine learning|ai analysis|multi-symbol|multi-instrument|multi-timeframe|multi-series|portfolio|equity protection|daily loss|weekly loss|partial close|break even|trailing stop|session filter|spread filter|time filter|order flow|market depth|custom rendering|sharpdx|report|export|licensing|database|webhook|api)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;
            var overloadedFeatures = advancedFeatures >= 6 &&
                (instructionSignals >= 12 || changeVerbs >= 14);

            if (requirementLength >= 4_500 || overloadedFeatures)
            {
                return RequestComplexityResult.Reject(
                    "This request combines too many requirements for one reliable build step.");
            }

            return RequestComplexityResult.Allowed;
        }

        if (fencedCodeLength == 0)
            return RequestComplexityResult.Allowed;

        if (fencedCodeLength >= 12_000 &&
            (numberedSteps >= 5 || bulletPoints >= 5))
        {
            return RequestComplexityResult.Reject(
                "Too many changes were requested against a large NinjaScript file.");
        }

        if (fencedCodeLength >= 25_000 &&
            (instructionSignals >= 4 || changeVerbs >= 8))
        {
            return RequestComplexityResult.Reject(
                "This request contains too many changes for a large NinjaScript project in one step.");
        }

        if (fencedCodeLength >= 15_000 &&
            requirementLength >= 1_800 &&
            (instructionSignals >= 5 || changeVerbs >= 10))
        {
            return RequestComplexityResult.Reject(
                "This request combines a large source file with too many changes.");
        }

        return RequestComplexityResult.Allowed;
    }
}

public sealed record RequestComplexityResult(
    bool Rejected,
    string Reason)
{
    public static RequestComplexityResult Allowed { get; } = new(false, "");

    public static RequestComplexityResult Reject(string reason) =>
        new(true, reason);
}
