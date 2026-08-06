using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using NinjaTrader_Xen.Models;
using NinjaTrader_Xen.Services;

namespace NinjaTrader_Xen.Endpoints;

public static class PreflightBuildEndpoints
{
    private const string NinjaTraderArchiveVersion = "8.0.0.9";

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
        app.MapPost(
            "/api/preflight/addon",
            DownloadAddOn).RequireAuthorization();
        return app;
    }

    private static IResult DownloadAddOn(
        PreflightBuildRequest request)
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

        var indicator = request.Task.Contains(
            "indicator",
            StringComparison.OrdinalIgnoreCase);
        var scriptType = indicator ? "Indicator" : "Strategy";
        var scriptFolder = indicator ? "Indicators" : "Strategies";
        var namespaceName =
            $"NinjaTrader.NinjaScript.{scriptFolder}";

        if (!Regex.IsMatch(
                request.Code,
                $@"\bnamespace\s+{Regex.Escape(namespaceName)}\b"))
        {
            return Results.BadRequest(new
            {
                message =
                    $"The source must use the {namespaceName} namespace."
            });
        }

        var classMatch = Regex.Match(
            request.Code,
            $@"\bpublic\s+(?:(?:sealed|partial|abstract)\s+)*class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?:[A-Za-z_][A-Za-z0-9_.]*\.)?{scriptType}\b");
        if (!classMatch.Success)
        {
            return Results.BadRequest(new
            {
                message =
                    $"Xen could not identify one public NinjaScript {scriptType} class."
            });
        }

        var className = classMatch.Groups["name"].Value;
        var downloadName = Regex.Replace(
            className,
            "Custom",
            string.Empty,
            RegexOptions.IgnoreCase);
        if (string.IsNullOrWhiteSpace(downloadName))
            downloadName = className;

        downloadName = ToReadableScriptName(downloadName);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(
                   output,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            WriteArchiveEntry(
                archive,
                "Info.xml",
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <NinjaTrader>
                  <Export>
                    <Version>{NinjaTraderArchiveVersion}</Version>
                  </Export>
                </NinjaTrader>
                """);
            WriteArchiveEntry(
                archive,
                $"{scriptFolder}/{className}.cs",
                request.Code.Trim());
        }

        return Results.File(
            output.ToArray(),
            "application/zip",
            $"{downloadName}-Addon.zip");
    }

    private static string ToReadableScriptName(string className)
    {
        var name = Regex.Replace(
            className,
            "([a-z0-9])([A-Z])",
            "$1 $2");
        name = Regex.Replace(
            name,
            "([A-Z]+)([A-Z][a-z])",
            "$1 $2");

        return Regex.Replace(name, @"\s+", " ").Trim();
    }

    private static void WriteArchiveEntry(
        ZipArchive archive,
        string path,
        string content)
    {
        var entry = archive.CreateEntry(
            path,
            CompressionLevel.Optimal);
        using var writer = new StreamWriter(
            entry.Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
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
