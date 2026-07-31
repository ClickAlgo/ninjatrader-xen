using Microsoft.AspNetCore.Diagnostics;
using System.Text.Json;

namespace NinjaTrader_Xen.Infrastructure;

internal sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            exception,
            "Unhandled request exception. Method={Method}, Path={Path}, TraceId={TraceId}",
            context.Request.Method,
            context.Request.Path,
            context.TraceIdentifier);

        if (context.Response.HasStarted)
            return false;

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.Headers.CacheControl = "no-store";

        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            await JsonSerializer.SerializeAsync(
                context.Response.Body,
                new
                {
                    error = "SERVER_ERROR",
                    message = "The request could not be completed."
                },
                cancellationToken: cancellationToken);
        }
        else
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(
                "An unexpected error occurred.",
                cancellationToken);
        }

        return true;
    }
}
