using Microsoft.Data.SqlClient;
using NinjaTrader_Xen.Models;
using NinjaTrader_Xen.Options;
using NinjaTrader_Xen.Services;
using System.Data;
using System.Security.Claims;
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
            "convert-indicator"
        };

    private static readonly HashSet<string> AllowedModels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "gpt-5.3-codex",
            "gpt-5.6-sol",
            "claude-sonnet-4-6",
            "claude-opus-5",
            "claude-fable-5",
            "deepseek-v4-pro"
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
        NinjaTraderKnowledgeRetriever knowledgeRetriever)
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

        var ragCategory = request.Task is
            "build-strategy" or "existing-strategy" or "convert-strategy"
                ? "Strategy"
                : "Indicator";
        var rag = await knowledgeRetriever.RetrieveAsync(
            request.Prompt,
            ragCategory,
            context.RequestAborted);
        var systemPrompt = systemPrompts.Build(request.Task);
        if (rag is not null && rag.Confident)
            systemPrompt += knowledgeRetriever.BuildSystemContext(rag);

        if (knowledgeRetriever.Options.ShowDebug &&
            rag?.Best is not null)
        {
            await WriteEvent(context, new
            {
                type = "rag.debug",
                title = rag.Best.Title,
                similarity = rag.Best.Similarity,
                used = rag.Confident,
                category = rag.Category
            });
        }

        var maximumOutputTokens = CalculateAffordableOutputTokens(
            configuration,
            request.Model,
            request.Prompt,
            request.History,
            systemPrompt,
            balanceGbp);

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

        var cleanHistory = (request.History ?? [])
            .TakeLast(12)
            .Where(turn =>
                turn.Role?.ToLowerInvariant() is "user" or "assistant" &&
                !string.IsNullOrWhiteSpace(turn.Content))
            .Select(turn => new ChatTurn(
                turn.Role.ToLowerInvariant(),
                turn.Content.Length > 40_000
                    ? turn.Content[..40_000]
                    : turn.Content))
            .ToList();

        var inputTokens = 0;
        var outputTokens = 0;
        var generatedCharacters = 0;
        try
        {
            await foreach (var streamEvent in aiClient.StreamAsync(
                request.Model,
                systemPrompt,
                cleanHistory,
                request.Prompt.Trim(),
                maximumOutputTokens,
                context.RequestAborted))
            {
                if (!string.IsNullOrEmpty(streamEvent.Delta))
                {
                    generatedCharacters += streamEvent.Delta.Length;
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
        decimal balanceGbp)
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
