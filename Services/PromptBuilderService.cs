using System.Net.Http.Json;
using System.Text.Json;

namespace NinjaTrader_Xen.Services;

public sealed class PromptBuilderService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<PromptBuilderService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public bool IsConfigured =>
        configuration.GetValue("PromptBuilder:Enabled", true) &&
        !string.IsNullOrWhiteSpace(configuration["DeepSeek:ApiKey"]);

    public async Task<PromptQualityResult> CheckAsync(
        string task,
        string prompt,
        CancellationToken cancellationToken)
    {
        if (!IsSupportedTask(task) || ShouldSkipCheck(prompt))
            return new(false, "");

        const string systemPrompt = """
            You assess first requests for a NinjaTrader 8 NinjaScript code generator.
            Recommend clarification only when missing requirements would materially change
            the implementation or make the requested trading tool unsafe or ambiguous.
            Do not reject a useful concise request merely because it is short. Familiar
            concepts such as RSI, SMA, EMA, stop loss, profit target, crossovers and
            configurable periods can use sensible NinjaScript defaults.

            Return JSON only:
            {"recommendPromptBuilder":true|false,"reason":"one short user-facing sentence"}
            """;

        try
        {
            var result = await CompleteJsonAsync<QualityResponse>(
                systemPrompt,
                $"Task: {task}\nRequest:\n{prompt}",
                180,
                cancellationToken);

            return new(
                result?.RecommendPromptBuilder == true,
                result?.Reason?.Trim() ?? "");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Prompt-quality check failed; allowing the request.");
            return new(false, "");
        }
    }

    public async Task<IReadOnlyList<PromptBuilderQuestion>> CreateQuestionsAsync(
        string task,
        string prompt,
        CancellationToken cancellationToken)
    {
        EnsureSupportedTask(task);

        const string systemPrompt = """
            You are the requirements assistant inside NinjaTrader Xen. Create 3 to 6
            concise questions that resolve only the important missing requirements before
            generating NinjaTrader 8 NinjaScript. Do not ask for facts already supplied.
            Prefer questions about signal rules, exits/risk, calculation timing, plots and
            configurable parameters as relevant to the selected task. Questions must be
            understandable to a trader and must not require programming knowledge.

            Return JSON only:
            {"questions":[{"id":"short-id","label":"Short label","question":"Question?","placeholder":"Example or guidance"}]}
            """;

        var result = await CompleteJsonAsync<QuestionsResponse>(
            systemPrompt,
            $"Task: {task}\nOriginal request:\n{prompt}",
            900,
            cancellationToken);

        var questions = result?.Questions?
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Id) &&
                !string.IsNullOrWhiteSpace(item.Question))
            .Take(6)
            .Select(item => new PromptBuilderQuestion(
                item.Id.Trim(),
                string.IsNullOrWhiteSpace(item.Label) ? "Requirement" : item.Label.Trim(),
                item.Question.Trim(),
                item.Placeholder?.Trim() ?? "Enter your preference"))
            .ToArray() ?? [];

        if (questions.Length == 0)
            throw new InvalidOperationException("The prompt builder returned no questions.");

        return questions;
    }

    public async Task<string> ComposeAsync(
        string task,
        string prompt,
        IReadOnlyList<PromptBuilderAnswer> answers,
        CancellationToken cancellationToken)
    {
        EnsureSupportedTask(task);

        const string systemPrompt = """
            Turn the trader's original request and clarification answers into a precise
            NinjaTrader 8 NinjaScript build request. Preserve the user's intent and never
            invent trading rules. If an answer says unspecified, ask Xen to use a sensible
            configurable default. Use compact Markdown headings appropriate to the task,
            such as Objective, Entry rules, Exit and risk rules, Parameters, and Behaviour.
            The output is a specification for a code generator, not code and not commentary.

            Return JSON only:
            {"improvedPrompt":"the complete structured build request"}
            """;

        var answerText = string.Join(
            "\n",
            answers
                .Where(item => !string.IsNullOrWhiteSpace(item.Question))
                .Take(6)
                .Select(item =>
                    $"- {item.Question.Trim()}: " +
                    $"{(string.IsNullOrWhiteSpace(item.Answer) ? "Not specified; use a sensible configurable default." : item.Answer.Trim())}"));

        var result = await CompleteJsonAsync<ComposeResponse>(
            systemPrompt,
            $"Task: {task}\nOriginal request:\n{prompt}\nClarifications:\n{answerText}",
            1200,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(result?.ImprovedPrompt))
            throw new InvalidOperationException("The prompt builder returned an empty specification.");

        return result.ImprovedPrompt.Trim();
    }

    private async Task<T?> CompleteJsonAsync<T>(
        string systemPrompt,
        string userPrompt,
        int maximumTokens,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(
            configuration.GetValue("PromptBuilder:TimeoutSeconds", 20)));

        var body = new
        {
            model = configuration["PromptBuilder:Model"] ?? "deepseek-v4-pro",
            stream = false,
            temperature = 0.1,
            max_tokens = maximumTokens,
            thinking = new { type = "disabled" },
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        var client = httpClientFactory.CreateClient("deepseek");
        using var response = await client.PostAsJsonAsync(
            "v1/chat/completions",
            body,
            JsonOptions,
            timeout.Token);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(timeout.Token),
            cancellationToken: timeout.Token);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return JsonSerializer.Deserialize<T>(ExtractJson(content), JsonOptions);
    }

    private static string ExtractJson(string? value)
    {
        var text = value?.Trim() ?? "";
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < start)
            throw new JsonException("The model did not return a JSON object.");
        return text[start..(end + 1)];
    }

    private static bool ShouldSkipCheck(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return true;

        return prompt.Contains("```", StringComparison.Ordinal) ||
               prompt.Length > 4000 ||
               prompt.Count(character => character == '\n') > 40;
    }

    private static bool IsSupportedTask(string task) =>
        task is "build-strategy" or "build-indicator";

    private static void EnsureSupportedTask(string task)
    {
        if (!IsSupportedTask(task))
            throw new ArgumentException("Prompt Builder is only available for new builds.");
    }

    private sealed record QualityResponse(bool RecommendPromptBuilder, string? Reason);
    private sealed record QuestionsResponse(List<QuestionResponse>? Questions);
    private sealed record QuestionResponse(
        string Id,
        string? Label,
        string Question,
        string? Placeholder);
    private sealed record ComposeResponse(string? ImprovedPrompt);
}

public sealed record PromptQualityResult(bool RecommendPromptBuilder, string Reason);
public sealed record PromptBuilderQuestion(
    string Id,
    string Label,
    string Question,
    string Placeholder);
public sealed record PromptBuilderAnswer(string Question, string? Answer);
