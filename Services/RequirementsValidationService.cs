using NinjaTrader_Xen.Models;
using System.Text;

namespace NinjaTrader_Xen.Services;

public sealed class RequirementsValidationService(
    AiStreamingClient aiClient,
    IWebHostEnvironment environment)
{
    public bool IsConfigured(string model) => aiClient.IsConfigured(model);

    public async Task<RequirementsValidationResult> ValidateAsync(
        RequirementsValidationRequest request,
        CancellationToken cancellationToken)
    {
        var templatePath = Path.Combine(
            environment.ContentRootPath,
            "SystemPrompts",
            "v1",
            "validate-requirements.txt");
        if (!File.Exists(templatePath))
        {
            throw new InvalidOperationException(
                "The requirements verification prompt is missing.");
        }

        var template = await File.ReadAllTextAsync(
            templatePath,
            cancellationToken);
        var prompt = template
            .Replace("{{TASK}}", request.Task, StringComparison.Ordinal)
            .Replace("{{REQUIREMENTS}}", request.Requirements, StringComparison.Ordinal)
            .Replace("{{CODE}}", request.Code, StringComparison.Ordinal);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var response = new StringBuilder();
            var inputTokens = 0;
            var outputTokens = 0;
            await foreach (var streamEvent in aiClient.StreamAsync(
                request.Model,
                "You are a NinjaTrader 8 NinjaScript requirements auditor.",
                [],
                prompt,
                null,
                3_000,
                cancellationToken))
            {
                if (!string.IsNullOrEmpty(streamEvent.Delta))
                    response.Append(streamEvent.Delta);
                if (streamEvent.Completed)
                {
                    inputTokens = streamEvent.InputTokens;
                    outputTokens = streamEvent.OutputTokens;
                }
            }

            if (response.Length == 0)
                continue;

            if (inputTokens <= 0)
                inputTokens = Math.Max(1, prompt.Length / 4);
            if (outputTokens <= 0)
                outputTokens = Math.Max(1, response.Length / 4);

            return new RequirementsValidationResult(
                response.ToString().Trim(),
                request.Model,
                inputTokens,
                outputTokens);
        }

        throw new InvalidOperationException(
            "Requirements verification returned an empty report.");
    }
}
