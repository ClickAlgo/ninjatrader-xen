using NinjaTrader_Xen.Models;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace NinjaTrader_Xen.Services;

public sealed class MoonshotStreamingClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IAiStreamingProvider
{
    private const string KimiModel = "kimi-k2.7-code";

    public string DisplayName => "Moonshot";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(configuration["Moonshot:ApiKey"]) ||
        !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("MOONSHOT_API_KEY"));

    public bool Supports(string model) =>
        string.Equals(model, KimiModel, StringComparison.OrdinalIgnoreCase);

    public bool SupportsImages(string model) => false;

    public async IAsyncEnumerable<AiStreamEvent> StreamAsync(
        string model,
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string prompt,
        AiImage? image,
        int maximumOutputTokens,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (image is not null)
        {
            throw new InvalidOperationException(
                "Kimi K2.7 Code does not support image uploads.");
        }

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };
        messages.AddRange(history.Select(turn => new
        {
            role = turn.Role.ToLowerInvariant(),
            content = turn.Content
        }));
        messages.Add(new { role = "user", content = prompt });

        var body = new
        {
            model,
            stream = true,
            stream_options = new { include_usage = true },
            max_tokens = maximumOutputTokens,
            messages
        };

        using var response = await SendAsync(body, cancellationToken);
        var completed = false;

        await foreach (var payload in StreamingJson.ReadSse(
            response,
            cancellationToken))
        {
            if (payload.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("delta", out var deltaObject) &&
                deltaObject.TryGetProperty("content", out var contentProperty))
            {
                var delta = contentProperty.GetString();
                if (!string.IsNullOrEmpty(delta))
                    yield return new AiStreamEvent(delta, 0, 0, false);
            }

            if (payload.TryGetProperty("usage", out var usage) &&
                usage.ValueKind == JsonValueKind.Object)
            {
                completed = true;
                yield return new AiStreamEvent(
                    null,
                    StreamingJson.ReadInt(usage, "prompt_tokens"),
                    StreamingJson.ReadInt(usage, "completion_tokens"),
                    true);
            }
        }

        if (!completed)
            yield return new AiStreamEvent(null, 0, 0, true);
    }

    private async Task<HttpResponseMessage> SendAsync(
        object body,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("moonshot");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json")
        };

        var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.IsSuccessStatusCode)
            return response;

        response.Dispose();
        throw new InvalidOperationException(
            $"Moonshot request failed with status {(int)response.StatusCode}.");
    }
}
