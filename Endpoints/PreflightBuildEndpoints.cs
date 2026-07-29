using NinjaTrader_Xen.Models;
using NinjaTrader_Xen.Services;

namespace NinjaTrader_Xen.Endpoints;

public static class PreflightBuildEndpoints
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

    public static IEndpointRouteBuilder MapPreflightBuildEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/preflight/build",
            Build).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> Build(
        PreflightBuildRequest request,
        NinjaTraderPreflightCompiler compiler,
        HttpContext context,
        ILogger<NinjaTraderPreflightCompiler> logger)
    {
        if (!AllowedTasks.Contains(request.Task))
        {
            return Results.BadRequest(new
            {
                message = "Select a valid NinjaTrader task."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Code) ||
            request.Code.Length > 500_000)
        {
            return Results.BadRequest(new
            {
                message =
                    "A complete NinjaScript source file of 500,000 characters or fewer is required."
            });
        }

        if (!compiler.TryGetConfigurationError(out var configurationError))
        {
            return Results.Json(
                new { message = configurationError },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var result = await compiler.BuildAsync(
                request.Code,
                context.RequestAborted);
            return Results.Ok(new
            {
                success = result.Success,
                errors = result.Errors,
                durationMilliseconds = result.DurationMilliseconds,
                message = result.Success
                    ? "Preflight build passed. Final compilation and testing in NinjaTrader are still required."
                    : "Preflight build failed. Review or repair the compiler errors below."
            });
        }
        catch (OperationCanceledException)
            when (context.RequestAborted.IsCancellationRequested)
        {
            return Results.Empty;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "The NinjaTrader preflight build infrastructure failed.");
            return Results.Problem(
                title: "Preflight build unavailable",
                detail:
                    "The NinjaTrader preflight build could not be completed. Please try again.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
