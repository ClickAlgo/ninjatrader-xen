using Microsoft.Data.SqlClient;
using NinjaTrader_Xen.Memory;
using NinjaTrader_Xen.Models;
using NinjaTrader_Xen.Options;
using NinjaTrader_Xen.Services;
using System.Data;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace NinjaTrader_Xen.Endpoints;

public static class ChatEndpoints
{
    private static readonly HashSet<string> AllowedTasks =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "build-strategy",
            "build-indicator",
            "existing-strategy",
            "existing-indicator",
            "convert-strategy",
            "convert-indicator",
            "analyse-backtest"
        };

    private static readonly HashSet<string> AllowedModels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "gpt-5.3-codex",
            "gpt-5.6-sol",
            "gpt-5.6-luna",
            "claude-sonnet-4-6",
            "claude-opus-5",
            "kimi-k2.7-code",
            "deepseek-v4-pro"
        };

    private static readonly HashSet<string> ImageTasks =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "build-indicator",
            "convert-indicator"
        };

    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat/stream", Stream).RequireAuthorization();
        return app;
    }

    private static async Task Stream(
        ChatRequest request,
        HttpContext context,
        IConfiguration configuration,
        AiStreamingClient aiClient,
        SystemPromptService systemPrompts,
        NinjaTraderKnowledgeRetriever knowledgeRetriever,
        IProjectMemoryStore projectMemoryStore,
        ILoggerFactory loggerFactory)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Append("X-Accel-Buffering", "no");

        if (!int.TryParse(context.User.FindFirstValue("sid"), out var subscriberId) ||
            subscriberId <= 0)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 60_000)
        {
            await WriteEvent(context, new { type = "error", message = "Enter a prompt of 60,000 characters or fewer." });
            await Complete(context);
            return;
        }

        if (request.ProjectId is null || request.ProjectId == Guid.Empty)
        {
            await WriteEvent(context, new
            {
                type = "error",
                message = "Project identity is required for conversation memory."
            });
            await Complete(context);
            return;
        }

        if (!AllowedTasks.Contains(request.Task))
        {
            await WriteEvent(context, new { type = "error", message = "Select a valid NinjaTrader task." });
            await Complete(context);
            return;
        }

        if (!AllowedModels.Contains(request.Model))
        {
            await WriteEvent(context, new { type = "error", message = "Select a supported AI model." });
            await Complete(context);
            return;
        }

        var complexity = TradingRequestComplexityPolicy.Evaluate(
            request.Task,
            request.Prompt);
        if (complexity.Rejected)
        {
            await WriteEvent(context, new
            {
                type = "error",
                code = "REQUEST_TOO_LARGE_FOR_SINGLE_STEP",
                message = "This request is too large for one reliable build. Use Prompt Builder to create shorter steps, build and test each step, then continue.",
                detail = complexity.Reason
            });
            await Complete(context);
            return;
        }

        if (!aiClient.IsConfigured(request.Model))
        {
            await WriteEvent(context, new
            {
                type = "error",
                message = "The selected AI provider is not configured on this server."
            });
            await Complete(context);
            return;
        }

        var (image, imageError) = ValidateImage(request.Image);
        if (imageError is not null)
        {
            await WriteEvent(context, new { type = "error", message = imageError });
            await Complete(context);
            return;
        }

        if (image is not null && !ImageTasks.Contains(request.Task))
        {
            await WriteEvent(context, new
            {
                type = "error",
                message = "Reference images are available only for Build Indicator and Convert Indicator."
            });
            await Complete(context);
            return;
        }

        if (image is not null && !aiClient.SupportsImages(request.Model))
        {
            await WriteEvent(context, new
            {
                type = "error",
                message = "The selected AI model does not support image uploads."
            });
            await Complete(context);
            return;
        }

        var connectionString = configuration.GetConnectionString("CodePilot")
            ?? throw new InvalidOperationException("ConnectionStrings:CodePilot is not configured.");

        var balanceGbp = await GetEligibleBalance(connectionString, subscriberId);
        if (balanceGbp <= 0)
        {
            await WriteEvent(context, new
            {
                type = "blocked",
                message = "Your account has no available credit. Add credit before using AI chat.",
                balanceGbp
            });
            await Complete(context);
            return;
        }

        RagRetrieval? rag = null;
        if (!request.Task.Equals(
                "analyse-backtest",
                StringComparison.OrdinalIgnoreCase))
        {
            var retrievalPrompt = string.IsNullOrWhiteSpace(
                request.RetrievalPrompt)
                    ? request.Prompt
                    : request.RetrievalPrompt.Trim();
            if (retrievalPrompt.Length > 60_000)
                retrievalPrompt = retrievalPrompt[..60_000];

            IReadOnlyList<string> ragCategories = request.Task is
                "build-strategy" or "existing-strategy" or "convert-strategy"
                    ? ["Strategy", "Indicator"]
                    : ["Indicator"];
            rag = await knowledgeRetriever.RetrieveAsync(
                retrievalPrompt,
                ragCategories,
                context.RequestAborted);
        }
        var systemPrompt = systemPrompts.Build(request.Task);
        if (rag is not null && rag.Confident)
            systemPrompt += knowledgeRetriever.BuildSystemContext(rag);

        var projectId = request.ProjectId.Value;
        var logger = loggerFactory.CreateLogger("ProjectMemory");
        try
        {
            await projectMemoryStore.SeedIfEmptyAsync(
                subscriberId,
                projectId,
                request.Task,
                request.History,
                context.RequestAborted);
            var memoryTurns = await projectMemoryStore.GetLatestTurnsAsync(
                subscriberId,
                projectId,
                5,
                context.RequestAborted);
            if (memoryTurns.Count > 0)
            {
                systemPrompt += "\n\n" +
                    PromptContextBuilder.BuildProjectMemoryTurnsBlock(
                        memoryTurns);
            }
        }
        catch (OperationCanceledException)
            when (context.RequestAborted.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not load project memory for {ProjectId}.",
                projectId);
        }

        string currentCode;
        try
        {
            currentCode = await GetLatestProjectCode(
                    connectionString,
                    subscriberId,
                    projectId,
                    context.RequestAborted) ??
                string.Empty;
        }
        catch (OperationCanceledException)
            when (context.RequestAborted.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not load authoritative code for {ProjectId}.",
                projectId);
            currentCode = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(currentCode))
        {
            currentCode = SqlProjectMemoryStore.ExtractLatestCode(
                request.History);
        }
        if (!string.IsNullOrWhiteSpace(currentCode))
        {
            systemPrompt += "\n\n" +
                PromptContextBuilder.BuildCurrentImplementationBlock(
                    currentCode);
        }

        if (knowledgeRetriever.Options.ShowDebug &&
            rag?.Best is not null)
        {
            var similarityThreshold =
                knowledgeRetriever.Options.SimilarityThreshold;
            await WriteEvent(context, new
            {
                type = "rag.debug",
                matches = rag.Matches.Select(match => new
                {
                    title = match.Title,
                    similarity = match.Similarity,
                    used = match.ForceInclude ||
                        match.Similarity >= similarityThreshold
                }),
                category = rag.Category
            });
        }

        IReadOnlyList<ChatTurn> cleanHistory = [];
        var maximumOutputTokens = CalculateAffordableOutputTokens(
            configuration,
            request.Model,
            request.Prompt,
            cleanHistory,
            systemPrompt,
            balanceGbp,
            image is not null);

        if (maximumOutputTokens < 800)
        {
            await WriteEvent(context, new
            {
                type = "blocked",
                message = "Your balance is too low for a useful response. Add credit before continuing.",
                balanceGbp
            });
            await Complete(context);
            return;
        }

        var inputTokens = 0;
        var outputTokens = 0;
        var generatedCharacters = 0;
        var assistantText = new StringBuilder();
        try
        {
            await foreach (var streamEvent in aiClient.StreamAsync(
                request.Model,
                systemPrompt,
                cleanHistory,
                request.Prompt.Trim(),
                image,
                maximumOutputTokens,
                context.RequestAborted))
            {
                if (!string.IsNullOrEmpty(streamEvent.Delta))
                {
                    generatedCharacters += streamEvent.Delta.Length;
                    assistantText.Append(streamEvent.Delta);
                    await WriteEvent(context, new
                    {
                        type = "response.output_text.delta",
                        delta = streamEvent.Delta
                    });
                }

                if (streamEvent.Completed)
                {
                    inputTokens = streamEvent.InputTokens;
                    outputTokens = streamEvent.OutputTokens;
                }
            }

            if (inputTokens <= 0)
            {
                var inputCharacters =
                    systemPrompt.Length +
                    request.Prompt.Length +
                    cleanHistory.Sum(turn => turn.Content.Length);
                inputTokens = Math.Max(1, (int)Math.Ceiling(inputCharacters / 4m));
                if (image is not null)
                    inputTokens += 2_000;
            }

            if (outputTokens <= 0 && generatedCharacters > 0)
            {
                outputTokens = Math.Max(
                    1,
                    (int)Math.Ceiling(generatedCharacters / 4m));
            }

            var totalCost = CalculateRetailCostGbp(
                configuration,
                request.Model,
                inputTokens,
                outputTokens);

            var charge = Math.Min(
                RoundUpToBillingPrecision(totalCost),
                balanceGbp);
            if (charge > 0)
                await DeductBalance(connectionString, subscriberId, charge);

            try
            {
                var responseText = assistantText.ToString();
                await projectMemoryStore.AppendTurnAsync(
                    new ProjectMemoryUpdate(
                        subscriberId,
                        projectId,
                        request.Task,
                        request.Prompt.Trim(),
                        responseText,
                        SqlProjectMemoryStore.ExtractLatestCode(
                            [new ChatTurn("assistant", responseText)])),
                    context.RequestAborted);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Could not save project memory for {ProjectId}.",
                    projectId);
            }

            var remainingBalance = await GetBalance(connectionString, subscriberId);
            await WriteEvent(context, new
            {
                type = "usage",
                totalCost,
                balanceGbp = remainingBalance
            });
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return;
        }
        catch (TimeoutException)
        {
            await WriteEvent(context, new
            {
                type = "error",
                message =
                    "Kimi stopped responding before the stream completed. Please try again."
            });
        }
        catch
        {
            await WriteEvent(context, new
            {
                type = "error",
                message = "The AI request could not be completed. Please try again."
            });
        }

        await Complete(context);
    }

    private static async Task<string?> GetLatestProjectCode(
        string connectionString,
        int subscriberId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT TOP (1) CodeText
            FROM dbo.ProjectRevisions
            WHERE ConversationId = @ProjectId
              AND SubscriberId = @SubscriberId
            ORDER BY CreatedUtc DESC, Id DESC;
            """, connection);
        command.Parameters.Add(
            "@ProjectId",
            SqlDbType.UniqueIdentifier).Value = projectId;
        command.Parameters.Add(
            "@SubscriberId",
            SqlDbType.Int).Value = subscriberId;
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task<decimal> GetEligibleBalance(string connectionString, int subscriberId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var entitlementCommand = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.Subscribers
            WHERE SubscriberId = @SubscriberId
              AND PlatformId = @PlatformId
              AND EmailVerified = 1
              AND Status = 1;
            """, connection))
        {
            entitlementCommand.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
            entitlementCommand.Parameters.Add("@PlatformId", SqlDbType.Int).Value = PlatformIds.NinjaTrader;
            if (Convert.ToInt32(await entitlementCommand.ExecuteScalarAsync()) != 1)
                return 0;
        }

        return await GetBalance(connection, subscriberId);
    }

    private static async Task<decimal> GetBalance(string connectionString, int subscriberId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        return await GetBalance(connection, subscriberId);
    }

    private static async Task<decimal> GetBalance(SqlConnection connection, int subscriberId)
    {
        await using var command = new SqlCommand("dbo.GetSubscriberCreditBalanceGbp", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? 0 : Convert.ToDecimal(value);
    }

    private static async Task DeductBalance(
        string connectionString,
        int subscriberId,
        decimal amountGbp)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("dbo.DeductSubscriberBalanceGbp", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
        var amount = command.Parameters.Add("@AmountGbp", SqlDbType.Decimal);
        amount.Precision = 10;
        amount.Scale = 4;
        amount.Value = amountGbp;
        await command.ExecuteNonQueryAsync();
    }

    private static decimal RoundUpToBillingPrecision(decimal amount) =>
        amount <= 0
            ? 0
            : Math.Ceiling(amount * 10_000m) / 10_000m;

    private static decimal CalculateRetailCostGbp(
        IConfiguration configuration,
        string model,
        int inputTokens,
        int outputTokens)
    {
        var pricing = configuration
            .GetSection($"Pricing:Models:{model}")
            .Get<ModelPricing>();

        if (pricing is null)
            return 0;

        var wholesaleUsd =
            inputTokens / 1_000_000m * pricing.InputPer1M +
            outputTokens / 1_000_000m * pricing.OutputPer1M;
        var usdToGbp = configuration.GetValue("Currency:UsdToGbp", 0.8m);
        var retailMargin = configuration.GetValue("Pricing:RetailMargin", 0.8m);
        if (retailMargin is <= 0 or >= 1)
            throw new InvalidOperationException("OpenAI retail margin must be between zero and one.");

        return Math.Round(wholesaleUsd * usdToGbp / (1 - retailMargin), 6);
    }

    private static int CalculateAffordableOutputTokens(
        IConfiguration configuration,
        string model,
        string prompt,
        IReadOnlyList<ChatTurn>? history,
        string systemPrompt,
        decimal balanceGbp,
        bool hasImage)
    {
        var pricing = configuration
            .GetSection($"Pricing:Models:{model}")
            .Get<ModelPricing>();
        if (pricing is null || pricing.OutputPer1M <= 0)
            return 0;

        var inputCharacters = prompt.Length +
            (history ?? []).TakeLast(12).Sum(turn => Math.Min(turn.Content?.Length ?? 0, 40_000)) +
            systemPrompt.Length;
        var estimatedInputTokens = Math.Max(1_000, inputCharacters / 4);
        if (hasImage)
            estimatedInputTokens += 2_000;
        var estimatedInputUsd = estimatedInputTokens / 1_000_000m * pricing.InputPer1M;

        var usdToGbp = configuration.GetValue("Currency:UsdToGbp", 0.8m);
        var retailMargin = configuration.GetValue("Pricing:RetailMargin", 0.8m);
        if (usdToGbp <= 0 || retailMargin is <= 0 or >= 1)
            return 0;

        var availableWholesaleUsd = balanceGbp * (1 - retailMargin) / usdToGbp * 0.85m;
        var availableOutputUsd = Math.Max(0, availableWholesaleUsd - estimatedInputUsd);
        var affordableTokens = decimal.Floor(
            availableOutputUsd / pricing.OutputPer1M * 1_000_000m);

        return (int)Math.Clamp(affordableTokens, 0, 10_000);
    }

    private static (AiImage? Image, string? Error) ValidateImage(
        ChatImageRequest? request)
    {
        if (request is null)
            return (null, null);

        var mediaType = request.Type?.Trim().ToLowerInvariant();
        if (mediaType is not ("image/png" or "image/jpeg" or "image/webp"))
            return (null, "Use a PNG, JPEG or WebP reference image.");

        if (string.IsNullOrWhiteSpace(request.Data) ||
            request.Data.Length > 4_200_000)
        {
            return (null, "The reference image is invalid or exceeds 3 MB.");
        }

        var expectedPrefix = $"data:{mediaType};base64,";
        if (!request.Data.StartsWith(
            expectedPrefix,
            StringComparison.OrdinalIgnoreCase))
        {
            return (null, "The reference image data does not match its file type.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(
                request.Data[expectedPrefix.Length..]);
        }
        catch (FormatException)
        {
            return (null, "The reference image could not be decoded.");
        }

        if (bytes.Length is 0 or > 3 * 1024 * 1024)
            return (null, "The reference image must be 3 MB or smaller.");

        if (!HasExpectedSignature(bytes, mediaType))
            return (null, "The reference image content does not match its file type.");

        var safeName = Path.GetFileName(request.Name ?? "reference-image");
        return (
            new AiImage(
                safeName,
                mediaType,
                Convert.ToBase64String(bytes)),
            null);
    }

    private static bool HasExpectedSignature(
        ReadOnlySpan<byte> bytes,
        string mediaType) =>
        mediaType switch
        {
            "image/png" =>
                bytes.Length >= 8 &&
                bytes[..8].SequenceEqual(
                    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" =>
                bytes.Length >= 3 &&
                bytes[0] == 0xFF &&
                bytes[1] == 0xD8 &&
                bytes[2] == 0xFF,
            "image/webp" =>
                bytes.Length >= 12 &&
                bytes[..4].SequenceEqual("RIFF"u8) &&
                bytes.Slice(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };

    private static async Task WriteEvent(HttpContext context, object value)
    {
        await context.Response.WriteAsync(
            $"data: {JsonSerializer.Serialize(value)}\n\n",
            context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }

    private static async Task Complete(HttpContext context)
    {
        await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }
}
