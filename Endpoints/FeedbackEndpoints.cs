using NinjaTrader_Xen.Services;
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
        return app;
    }

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
}
