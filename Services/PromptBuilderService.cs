using System.Net.Http.Json;
using System.Text.Json;

namespace NinjaTrader_Xen.Services;

public sealed class PromptBuilderService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    RequestRouterService requestRouter,
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
        bool hasCurrentCode,
        string? previousAssistantResponse,
        CancellationToken cancellationToken)
    {
        if (hasCurrentCode)
            return new(false, "", false);

        var complexity = TradingRequestComplexityPolicy.Evaluate(task, prompt);
        if (complexity.Rejected)
        {
            return new(
                true,
                "This request is too large for one reliable build. Use Prompt Builder to split it into shorter build-test-build steps.",
                true);
        }

        if (!IsSupportedTask(task) || ShouldSkipCheck(prompt))
            return new(false, "", false);

        try
        {
            var result = await requestRouter.RouteAsync(
                task,
                prompt,
                hasCurrentCode,
                previousAssistantResponse,
                cancellationToken);
            return new(
                result.RecommendPromptBuilder,
                string.IsNullOrWhiteSpace(result.Reason)
                    ? "This request may benefit from clearer requirements before implementation."
                    : result.Reason.Trim(),
                false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Prompt-quality check failed open.");
            return new(false, "", false);
        }
    }

    public async Task<IReadOnlyList<PromptBuilderQuestion>> CreateQuestionsAsync(
        string task,
        string prompt,
        CancellationToken cancellationToken)
    {
        EnsureSupportedTask(task);
        const string systemPrompt = """
            You are the requirements assistant inside NinjaTrader Xen. Ask the minimum
            number of concise clarification questions needed before planning NinjaTrader 8
            NinjaScript:
            - Return exactly 1 question when only 1 material ambiguity needs resolving.
            - Return exactly 2 questions when 2 material ambiguities need resolving.
            - Return exactly 3 questions only when 3 material ambiguities need resolving.

            Never pad the list to reach 3. Never ask an optional, low-value or already
            answered question merely to increase the count. Minor details that do not
            materially change the implementation should use sensible NinjaScript baseline
            assumptions in the Build Plan instead of becoming questions.

            Do not ask for facts already supplied. Prefer questions about signal rules,
            exits and risk, calculation timing, plots, order handling and configurable
            parameters only when the answer would materially change the implementation.
            Questions must be understandable to a trader and require no programming
            knowledge.

            Return JSON only:
            {"questions":[{"id":"short-id","label":"Short label","question":"Question?","placeholder":"Example or guidance"}]}
            """;

        var result = await CompleteJsonAsync<QuestionsResponse>(
            systemPrompt,
            $"Task: {task}\nOriginal request:\n{prompt}",
            900,
            cancellationToken);

        var questions = result?.Questions?
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) &&
                           !string.IsNullOrWhiteSpace(item.Question))
            .Take(3)
            .Select(item => new PromptBuilderQuestion(
                item.Id.Trim(),
                string.IsNullOrWhiteSpace(item.Label) ? "Requirement" : item.Label.Trim(),
                item.Question.Trim(),
                item.Placeholder?.Trim() ?? "Enter your preference"))
            .ToArray() ?? [];

        if (questions.Length == 0)
            throw new InvalidOperationException("The Prompt Builder returned no questions.");
        return questions;
    }

    public async Task<PromptBuilderPlan> ComposeAsync(
        string task,
        string prompt,
        IReadOnlyList<PromptBuilderAnswer> answers,
        CancellationToken cancellationToken)
    {
        EnsureSupportedTask(task);
        if (answers.Count is < 1 or > 3)
            throw new ArgumentException("Provide between 1 and 3 answers.");

        const string systemPrompt = """
            You are Xen Build Planner, a requirements and prompt-planning assistant for
            NinjaTrader 8 NinjaScript projects.

            Convert the user's trading-tool idea into a numbered sequence of small
            implementation prompts that can be submitted to NinjaTrader Xen one at a time.
            The user must compile and test every iteration before continuing. You plan the
            work. Never generate source code and never execute the implementation prompts.

            Preserve the user's intent. Never invent trading rules, add unrequested
            features, provide trading advice, or promise profitability or performance.
            For optional details, prefer sensible NinjaScript baseline assumptions and list
            material assumptions separately.

            Create 3 to 8 prompts, using fewer prompts for simple projects. Order them by
            dependency: minimal working foundation; essential behaviour or data; dependent
            calculations, order handling or trading functions; then secondary controls,
            presentation and refinements.

            Prompt 1 must create a minimal working NinjaTrader 8 NinjaScript implementation
            that compiles and can be tested. It must identify whether a new Strategy or
            Indicator is being created.

            Every prompt must have one primary objective, be concise but unambiguous, make
            independently testable progress, contain all requirements for that iteration,
            request the complete compile-ready NinjaScript C# file, and stop after the
            current iteration. Do not include the future roadmap.

            Every prompt after Prompt 1 must begin exactly:
            Update the existing implementation created in the previous step.

            Every later prompt must instruct Xen to preserve all existing working
            behaviour, keep implemented features unchanged, add only the current feature,
            avoid unrelated changes, redesigns and refactoring, return the complete updated
            compile-ready NinjaScript C# file, stop after the current iteration, and not
            implement or anticipate later stages.

            Where relevant, explicitly define materially ambiguous indicator calculations
            and plots, Calculate mode, historical versus real-time processing, entries and
            exits, long and short handling, position and order state, managed versus
            unmanaged orders, protective orders, quantity and risk, tick/point conversions,
            sessions, resets, time zone, instrument/data-series scope, empty-data and
            zero-division handling, and whether historical values can change.

            Do not split one calculation or control into unusably small fragments. Do not
            combine unrelated major stages.

            Return JSON only in this exact shape:
            {
              "explanation":"A concise explanation of why the project is divided this way and how to use the sequence.",
              "assumptions":["Material assumption 1"],
              "prompts":[{"title":"Short descriptive title","prompt":"Complete standalone implementation prompt for NinjaTrader Xen"}]
            }
            """;

        var answerText = string.Join("\n", answers.Take(3).Select(item =>
            $"- {item.Question.Trim()}: " +
            (string.IsNullOrWhiteSpace(item.Answer)
                ? "Not specified; use a sensible configurable default."
                : item.Answer.Trim())));

        var result = await CompleteJsonAsync<PlanResponse>(
            systemPrompt,
            $"Task: {task}\nOriginal request:\n{prompt}\nClarifications:\n{answerText}",
            4000,
            cancellationToken);

        var prompts = result?.Prompts?
            .Where(item => !string.IsNullOrWhiteSpace(item.Title) &&
                           !string.IsNullOrWhiteSpace(item.Prompt))
            .Take(8)
            .Select(item => new PromptBuilderStep(item.Title.Trim(), item.Prompt.Trim()))
            .ToArray() ?? [];

        if (prompts.Length is < 3 or > 8)
            throw new InvalidOperationException(
                "The Prompt Builder did not return between 3 and 8 implementation prompts.");

        return new PromptBuilderPlan(
            string.IsNullOrWhiteSpace(result?.Explanation)
                ? "Build and test the project in small, ordered iterations."
                : result.Explanation.Trim(),
            result?.Assumptions?
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Take(12)
                .ToArray() ?? [],
            prompts);
    }

    public async Task<IReadOnlyList<string>> SuggestAnswersAsync(
        string task,
        string prompt,
        IReadOnlyList<string> questions,
        CancellationToken cancellationToken)
    {
        EnsureSupportedTask(task);
        if (questions.Count is < 1 or > 3)
            throw new ArgumentException("Provide between 1 and 3 questions.");

        const string systemPrompt = """
            You provide editable baseline requirement suggestions for NinjaTrader Xen's
            Prompt Builder. Suggest one concise answer for every supplied question.

            Use conservative, configurable and testable NinjaScript behaviour. Preserve
            the original request. Do not claim any rule is best, optimal or profitable.
            Do not give trading advice. Do not generate source code. When several sensible
            interpretations exist, choose one straightforward baseline that a user can
            understand and edit before creating a Build Plan.

            Return JSON only in the same order as the questions:
            {"answers":["Editable baseline answer 1","Editable baseline answer 2"]}
            """;

        var questionText = string.Join("\n", questions.Select(
            (question, index) => $"{index + 1}. {question.Trim()}"));
        var result = await CompleteJsonAsync<SuggestionsResponse>(
            systemPrompt,
            $"Task: {task}\nOriginal request:\n{prompt}\nQuestions:\n{questionText}",
            1000,
            cancellationToken);
        var answers = result?.Answers?
            .Select(answer => answer?.Trim() ?? "")
            .Take(questions.Count)
            .ToArray() ?? [];
        if (answers.Length != questions.Count || answers.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException(
                "Prompt Builder did not return a suggestion for every question.");
        return answers;
    }

    private async Task<T?> CompleteJsonAsync<T>(
        string systemPrompt,
        string userPrompt,
        int maximumTokens,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("PromptBuilder:TimeoutSeconds", 20), 5, 60)));
        var body = new
        {
            model = configuration["PromptBuilder:Model"] ?? "deepseek-v4-pro",
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.1,
            max_tokens = maximumTokens,
            stream = false,
            response_format = new { type = "json_object" },
            thinking = new { type = "disabled" }
        };
        var client = httpClientFactory.CreateClient("deepseek");
        using var response = await client.PostAsJsonAsync(
            "v1/chat/completions", body, JsonOptions, timeout.Token);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(timeout.Token),
            cancellationToken: timeout.Token);
        var content = document.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString();
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

    private static bool ShouldSkipCheck(string prompt) =>
        string.IsNullOrWhiteSpace(prompt) ||
        prompt.Contains("```", StringComparison.Ordinal) ||
        prompt.Count(character => character == '\n') > 40;

    private static bool IsSupportedTask(string task) =>
        task is "build-strategy" or "build-indicator";

    private static void EnsureSupportedTask(string task)
    {
        if (!IsSupportedTask(task))
            throw new ArgumentException("Prompt Builder is only available for new builds.");
    }

    private sealed record QuestionsResponse(List<QuestionResponse>? Questions);
    private sealed record SuggestionsResponse(List<string?>? Answers);
    private sealed record QuestionResponse(string Id, string? Label, string Question, string? Placeholder);
    private sealed record PlanResponse(
        string? Explanation,
        List<string>? Assumptions,
        List<PlanStepResponse>? Prompts);
    private sealed record PlanStepResponse(string Title, string Prompt);
}

public sealed record PromptQualityResult(
    bool RecommendPromptBuilder,
    string Reason,
    bool Required);
public sealed record PromptBuilderQuestion(string Id, string Label, string Question, string Placeholder);
public sealed record PromptBuilderAnswer(string Question, string? Answer);
public sealed record PromptBuilderPlan(
    string Explanation,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<PromptBuilderStep> Prompts);
public sealed record PromptBuilderStep(string Title, string Prompt);
