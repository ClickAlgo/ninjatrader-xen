using NinjaTrader_Xen.Services;

namespace NinjaTrader_Xen.Endpoints;

public static class PromptBuilderEndpoints
{
    public static IEndpointRouteBuilder MapPromptBuilderEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/prompt-builder").RequireAuthorization();
        group.MapPost("/check", Check);
        group.MapPost("/questions", Questions);
        group.MapPost("/compose", Compose);
        return app;
    }

    private static async Task<IResult> Check(
        PromptRequest request,
        PromptBuilderService service,
        CancellationToken cancellationToken)
    {
        var validation = ValidatePrompt(request);
        if (validation is not null)
            return validation;

        var result = await service.CheckAsync(
            request.Task,
            request.Prompt,
            cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> Questions(
        PromptRequest request,
        PromptBuilderService service,
        CancellationToken cancellationToken)
    {
        var validation = ValidatePrompt(request);
        if (validation is not null)
            return validation;
        if (!service.IsConfigured)
            return Results.Problem(
                "Prompt Builder is not configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        try
        {
            var questions = await service.CreateQuestionsAsync(
                request.Task,
                request.Prompt,
                cancellationToken);
            return Results.Ok(new { questions });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Problem(
                "Prompt Builder took too long to respond.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch
        {
            return Results.Problem(
                "Prompt Builder could not prepare clarification questions.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> Compose(
        ComposeRequest request,
        PromptBuilderService service,
        CancellationToken cancellationToken)
    {
        var validation = ValidatePrompt(new PromptRequest(request.Task, request.Prompt));
        if (validation is not null)
            return validation;
        if (request.Answers is null || request.Answers.Count is < 1 or > 6)
            return Results.BadRequest(new { message = "Provide between 1 and 6 answers." });
        if (request.Answers.Any(item =>
                item.Question?.Length > 500 || item.Answer?.Length > 2000))
            return Results.BadRequest(new { message = "A clarification answer is too long." });

        try
        {
            var improvedPrompt = await service.ComposeAsync(
                request.Task,
                request.Prompt,
                request.Answers,
                cancellationToken);
            return Results.Ok(new { improvedPrompt });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Problem(
                "Prompt Builder took too long to respond.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch
        {
            return Results.Problem(
                "Prompt Builder could not create the specification.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static IResult? ValidatePrompt(PromptRequest request)
    {
        if (request.Task is not ("build-strategy" or "build-indicator"))
            return Results.BadRequest(new { message = "Prompt Builder is only available for new builds." });
        if (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 60000)
            return Results.BadRequest(new { message = "Enter a valid request." });
        return null;
    }

    public sealed record PromptRequest(string Task, string Prompt);
    public sealed record ComposeRequest(
        string Task,
        string Prompt,
        List<PromptBuilderAnswer> Answers);
}
