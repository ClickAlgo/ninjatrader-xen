using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace NinjaTrader_Xen.Services;

public sealed record RequestRouteDecision(string Intent, bool UseRag,
    bool StoreAsRequirement, bool RecommendPromptBuilder,
    string Reason, bool UsedFallback = false);

public sealed class RequestRouterService(
    AiStreamingClient aiClient, IConfiguration configuration,
    IMemoryCache cache, IWebHostEnvironment environment,
    ILogger<RequestRouterService> logger)
{
    private static readonly Regex FencedCode = new("```[\\s\\S]*?```", RegexOptions.Compiled);
    private readonly string _classifierPrompt = File.ReadAllText(Path.Combine(
        environment.ContentRootPath, "SystemPrompts", "v1", "request-router.txt")).Trim();

    public async Task<RequestRouteDecision> RouteAsync(string task, string prompt,
        bool hasCurrentCode, string? previousAssistantResponse,
        CancellationToken cancellationToken)
    {
        var fallback = CreateFallback(task, hasCurrentCode);
        if (!configuration.GetValue("RequestRouter:Enabled", true)) return fallback;
        var model = configuration["RequestRouter:Model"] ?? "gpt-5.6-luna";
        if (!aiClient.IsConfigured(model)) return fallback;

        var safePrompt = BuildSafeRequestSummary(prompt);
        if (string.IsNullOrWhiteSpace(safePrompt)) return fallback;
        var previousSummary = SummarizePreviousResponse(previousAssistantResponse);
        var source = $"{task}\n{hasCurrentCode}\n{safePrompt}\n{previousSummary}";
        var key = "request-route:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        if (cache.TryGetValue<RequestRouteDecision>(key, out var cached) && cached is not null)
            return cached;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("RequestRouter:TimeoutSeconds", 8), 2, 20)));
        var routerPrompt = $"Task: {task}\nExisting code: {(hasCurrentCode ? "yes" : "no")}\nPrevious response summary: {(previousSummary.Length == 0 ? "none" : previousSummary)}\nSanitized request summary:\n{safePrompt}";
        try
        {
            var text = new StringBuilder();
            await foreach (var item in aiClient.StreamAsync(model, _classifierPrompt, [],
                routerPrompt, null, Math.Clamp(configuration.GetValue(
                    "RequestRouter:MaxOutputTokens", 220), 120, 500), timeout.Token))
                if (!string.IsNullOrEmpty(item.Delta)) text.Append(item.Delta);

            var parsed = Parse(text.ToString());
            if (parsed is null) return fallback;
            var intent = NormalizeIntent(parsed.Intent);
            var decision = ApplyIntentPolicy(intent,
                parsed.RecommendPromptBuilder,
                parsed.Reason);
            cache.Set(key, decision, TimeSpan.FromMinutes(2));
            return decision;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Request router timed out; safe fallback routing was used.");
            return fallback;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Request router failed; safe fallback routing was used.");
            return fallback;
        }
    }

    internal static string BuildSafeRequestSummary(string prompt)
    {
        var value = FencedCode.Replace(prompt, " [source omitted] ");
        var lines = value.Split('\n').Where(line =>
            !line.Contains("using ", StringComparison.Ordinal) &&
            !line.Contains("namespace ", StringComparison.Ordinal) &&
            !line.Contains("public class ", StringComparison.Ordinal) &&
            !line.Contains("protected override", StringComparison.Ordinal) &&
            line.Count(character => character is '{' or '}' or ';') < 3);
        value = Regex.Replace(string.Join(' ', lines), "\\s+", " ").Trim();
        return value.Length <= 1800 ? value : value[..1800];
    }

    internal static string SummarizePreviousResponse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return "";
        var value = FencedCode.Replace(response, " ");
        value = Regex.Replace(value, "\\s+", " ").Trim();
        return value.Length <= 400 ? value : value[..400];
    }

    internal static RequestRouteDecision CreateFallback(string task, bool hasCode)
    {
        if (task.Equals("analyse-backtest", StringComparison.OrdinalIgnoreCase))
            return new("analysis", false, false, false, "Analysis fallback.", true);
        if (!hasCode && task is "build-strategy" or "build-indicator")
            return new("build", true, true, false, "First-build fallback.", true);
        return new("question", false, false, false, "Established-project fallback.", true);
    }

    internal static RequestRouteDecision ApplyIntentPolicy(
        string intent,
        bool recommendPromptBuilder,
        string? reason)
    {
        var ragEligible = intent is "build" or "modify" or "convert";
        var durable = intent is "build" or "modify" or "convert";
        var awaitingClarification = durable && recommendPromptBuilder;
        var useRag = ragEligible && !awaitingClarification;
        return new(intent, useRag, durable && !awaitingClarification,
            recommendPromptBuilder && durable,
            (reason ?? "").Trim());
    }

    private static RouterResponse? Parse(string value)
    {
        var text = value.Trim();
        if (text.StartsWith("```"))
        {
            var first = text.IndexOf('\n'); var last = text.LastIndexOf("```", StringComparison.Ordinal);
            if (first >= 0 && last > first) text = text[(first + 1)..last].Trim();
        }
        try { return JsonSerializer.Deserialize<RouterResponse>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { return null; }
    }

    private static string NormalizeIntent(string? value) => value?.Trim().ToLowerInvariant() switch
    { "build" => "build", "modify" => "modify", "convert" => "convert",
      "repair" => "repair", "analysis" => "analysis", _ => "question" };

    private sealed record RouterResponse(string? Intent,
        bool RecommendPromptBuilder, string? Reason);
}
