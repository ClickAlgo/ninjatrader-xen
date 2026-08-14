using Microsoft.Data.SqlClient;
using NinjaTrader_Xen.Services;
using System.Data;
using System.Security.Claims;

namespace NinjaTrader_Xen.Endpoints;

public static class FeedbackEndpoints
{
    private static readonly HashSet<string> AllowedTypes =
        new(StringComparer.OrdinalIgnoreCase) { "feedback", "bug" };

    public static IEndpointRouteBuilder MapFeedbackEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/feedback", Submit).RequireAuthorization();
        app.MapPost("/api/model-feedback", SaveModelFeedback)
            .RequireAuthorization();
        return app;
    }

    private static async Task<IResult> SaveModelFeedback(
        ModelFeedbackRequest request,
        HttpContext context,
        IConfiguration configuration,
        ILogger<Program> logger)
    {
        if (!int.TryParse(
                context.User.FindFirstValue("sid"),
                out var subscriberId) ||
            subscriberId <= 0)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Model) ||
            string.IsNullOrWhiteSpace(request.Task))
        {
            return Results.BadRequest(new
            {
                message = "Model and task are required."
            });
        }

        var connectionString = configuration.GetConnectionString("CodePilot")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:CodePilot is not configured.");

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new SqlCommand("""
                INSERT INTO dbo.ModelFeedback
                (
                    FeedbackId,
                    SubscriberId,
                    ConversationId,
                    SessionId,
                    Model,
                    Task,
                    WasHelpful,
                    AppVersion,
                    Comment,
                    UserPrompt,
                    AssistantOutput,
                    CreatedUtc
                )
                VALUES
                (
                    @FeedbackId,
                    @SubscriberId,
                    @ConversationId,
                    @SessionId,
                    @Model,
                    @Task,
                    @WasHelpful,
                    @AppVersion,
                    @Comment,
                    @UserPrompt,
                    @AssistantOutput,
                    SYSUTCDATETIME()
                );
                """, connection);
            command.Parameters.Add("@FeedbackId", SqlDbType.UniqueIdentifier)
                .Value = Guid.NewGuid();
            command.Parameters.Add("@SubscriberId", SqlDbType.Int)
                .Value = subscriberId;
            command.Parameters.Add("@ConversationId", SqlDbType.UniqueIdentifier)
                .Value = (object?)request.ConversationId ?? DBNull.Value;
            command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier)
                .Value = DBNull.Value;
            command.Parameters.Add("@Model", SqlDbType.NVarChar, 100)
                .Value = request.Model.Trim();
            command.Parameters.Add("@Task", SqlDbType.NVarChar, 100)
                .Value = request.Task.Trim();
            command.Parameters.Add("@WasHelpful", SqlDbType.Bit)
                .Value = request.WasHelpful;
            command.Parameters.Add("@AppVersion", SqlDbType.NVarChar, 50)
                .Value = DbValue(request.AppVersion);
            command.Parameters.Add("@Comment", SqlDbType.NVarChar, -1)
                .Value = "NinjaTrader Xen";
            command.Parameters.Add("@UserPrompt", SqlDbType.NVarChar, -1)
                .Value = DbValue(request.UserPrompt);
            command.Parameters.Add("@AssistantOutput", SqlDbType.NVarChar, -1)
                .Value = DbValue(request.AssistantOutput);

            await command.ExecuteNonQueryAsync(context.RequestAborted);
            return Results.Ok(new { success = true });
        }
        catch (SqlException exception)
        {
            logger.LogError(
                exception,
                "Failed to save model feedback | Subscriber={SubscriberId} | Model={Model}",
                subscriberId,
                request.Model);
            return Results.Problem(
                "Your vote could not be saved. Please try again.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static object DbValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static async Task<IResult> Submit(
        FeedbackRequest request,
        HttpContext context,
        AccountEmailSender emailSender,
        ILogger<Program> logger)
    {
        if (!int.TryParse(
                context.User.FindFirstValue("sid"),
                out var subscriberId) ||
            subscriberId <= 0)
        {
            return Results.Unauthorized();
        }

        var type = request.Type?.Trim().ToLowerInvariant() ?? "";
        var comment = request.Comment?.Trim() ?? "";
        if (!AllowedTypes.Contains(type))
            return Results.BadRequest(new { message = "Select a valid report type." });
        if (comment.Length is < 5 or > 2000)
            return Results.BadRequest(new
            {
                message = "Enter between 5 and 2,000 characters."
            });

        try
        {
            await emailSender.SendFeedbackAsync(
                type,
                comment,
                subscriberId,
                context.User.FindFirstValue(ClaimTypes.Email) ?? "unknown",
                context.Request.Headers.UserAgent.ToString(),
                request.IncludeDiagnostics
                    ? new FeedbackDiagnostics(
                        request.ProjectId,
                        Limit(request.Model, 100),
                        Limit(request.Task, 100),
                        Limit(request.AppVersion, 50),
                        Limit(request.UserPrompt, 60_000),
                        Limit(request.AssistantOutput, 100_000),
                        Limit(request.LatestCode, 100_000))
                    : null);
            return Results.Ok(new { success = true });
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Feedback submission failed | Subscriber={SubscriberId} | Type={Type}",
                subscriberId,
                type);
            return Results.Problem(
                "Your feedback could not be sent. Please try again.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static string? Limit(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = value.Trim();
        return text.Length <= maximumLength
            ? text
            : text[..maximumLength];
    }

    public sealed record FeedbackRequest(
        string? Type,
        string? Comment,
        bool IncludeDiagnostics,
        Guid? ProjectId,
        string? Model,
        string? Task,
        string? AppVersion,
        string? UserPrompt,
        string? AssistantOutput,
        string? LatestCode);

    public sealed record ModelFeedbackRequest(
        Guid? ConversationId,
        string? Model,
        string? Task,
        bool WasHelpful,
        string? AppVersion,
        string? UserPrompt,
        string? AssistantOutput);
}
