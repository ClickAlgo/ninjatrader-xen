using Microsoft.Data.SqlClient;
using NinjaTrader_Xen.Models;
using NinjaTrader_Xen.Options;
using NinjaTrader_Xen.Services;
using System.Data;
using System.Security.Claims;

namespace NinjaTrader_Xen.Endpoints;

public static class RequirementsEndpoints
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
            "gpt-5.6-luna",
            "claude-sonnet-4-6",
            "claude-opus-5",
            "deepseek-v4-pro",
            "kimi-k2.7-code"
        };

    public static IEndpointRouteBuilder MapRequirementsEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/requirements/validate",
            Validate).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> Validate(
        RequirementsValidationRequest request,
        HttpContext context,
        IConfiguration configuration,
        RequirementsValidationService service,
        ILogger<RequirementsValidationService> logger)
    {
        if (!int.TryParse(
            context.User.FindFirstValue("sid"),
            out var subscriberId) ||
            subscriberId <= 0)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Requirements) ||
            request.Requirements.Length > 100_000)
        {
            return Results.BadRequest(new
            {
                message = "Enter requirements of 100,000 characters or fewer."
            });
        }
        if (string.IsNullOrWhiteSpace(request.Code) ||
            request.Code.Length > 500_000)
        {
            return Results.BadRequest(new
            {
                message = "A complete source file of 500,000 characters or fewer is required."
            });
        }
        if (!AllowedTasks.Contains(request.Task))
            return Results.BadRequest(new { message = "Select a valid NinjaTrader task." });
        if (!AllowedModels.Contains(request.Model))
            return Results.BadRequest(new { message = "Select a supported AI model." });
        if (!service.IsConfigured(request.Model))
        {
            return Results.BadRequest(new
            {
                message = "The selected AI provider is not configured for requirements verification."
            });
        }

        var connectionString = configuration.GetConnectionString("CodePilot")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:CodePilot is not configured.");
        var balance = await GetEligibleBalance(
            connectionString,
            subscriberId,
            request.ProjectId);
        if (balance <= 0)
        {
            return Results.BadRequest(new
            {
                message = "Your Xen credit has run out. Please top up to continue.",
                balanceGbp = balance
            });
        }

        RequirementsValidationResult result;
        try
        {
            result = await service.ValidateAsync(
                request,
                context.RequestAborted);
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains(
                "empty report",
                StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                exception,
                "Model {Model} returned no requirements verification report.",
                request.Model);
            return Results.Problem(
                detail:
                    "The selected model returned no verification report. Please try again or choose another model.",
                statusCode: StatusCodes.Status502BadGateway,
                title: "Requirements verification could not be completed");
        }
        catch (TimeoutException exception)
        {
            logger.LogWarning(
                exception,
                "Model {Model} stopped streaming during requirements verification.",
                request.Model);
            return Results.Problem(
                detail:
                    "The selected model stopped responding before verification completed. Please try again or choose another model.",
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "Requirements verification timed out");
        }
        var totalCost = CalculateRetailCostGbp(
            configuration,
            request.Model,
            result.InputTokens,
            result.OutputTokens);
        var charge = Math.Min(RoundUp(totalCost), balance);
        if (charge > 0)
            await DeductBalance(connectionString, subscriberId, charge);
        var remaining = await GetBalance(connectionString, subscriberId);

        return Results.Ok(new
        {
            message = result.Message,
            model = result.Model,
            totalCost,
            balanceGbp = remaining
        });
    }

    private static async Task<decimal> GetEligibleBalance(
        string connectionString,
        int subscriberId,
        Guid? projectId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var command = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.Subscribers
            WHERE SubscriberId = @SubscriberId
              AND PlatformId = @PlatformId
              AND EmailVerified = 1
              AND Status = 1;
            """, connection))
        {
            command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
            command.Parameters.Add("@PlatformId", SqlDbType.Int).Value =
                PlatformIds.NinjaTrader;
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 1)
                return 0;
        }

        if (projectId.HasValue)
        {
            await using var projectCommand = new SqlCommand("""
                SELECT COUNT(1)
                FROM dbo.SavedConversations
                WHERE ConversationId = @ProjectId
                  AND SubscriberId = @SubscriberId;
                """, connection);
            projectCommand.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier)
                .Value = projectId.Value;
            projectCommand.Parameters.Add("@SubscriberId", SqlDbType.Int)
                .Value = subscriberId;
            if (Convert.ToInt32(await projectCommand.ExecuteScalarAsync()) != 1)
                return 0;
        }

        return await GetBalance(connection, subscriberId);
    }

    private static async Task<decimal> GetBalance(
        string connectionString,
        int subscriberId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        return await GetBalance(connection, subscriberId);
    }

    private static async Task<decimal> GetBalance(
        SqlConnection connection,
        int subscriberId)
    {
        await using var command = new SqlCommand(
            "dbo.GetSubscriberCreditBalanceGbp",
            connection)
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
        decimal amount)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "dbo.DeductSubscriberBalanceGbp",
            connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
        var amountParameter = command.Parameters.Add(
            "@AmountGbp",
            SqlDbType.Decimal);
        amountParameter.Precision = 10;
        amountParameter.Scale = 4;
        amountParameter.Value = amount;
        await command.ExecuteNonQueryAsync();
    }

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
            throw new InvalidOperationException(
                "Retail margin must be between zero and one.");
        return Math.Round(
            wholesaleUsd * usdToGbp / (1 - retailMargin),
            6);
    }

    private static decimal RoundUp(decimal amount) =>
        amount <= 0
            ? 0
            : Math.Ceiling(amount * 10_000m) / 10_000m;
}
