using NinjaTrader_Xen.Models;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace NinjaTrader_Xen.Services;

public sealed class DeepSeekStreamingClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IAiStreamingProvider
{
    public string DisplayName => "DeepSeek";
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(configuration["DeepSeek:ApiKey"]);

    public bool Supports(string model) =>
        model.StartsWith("deepseek-", StringComparison.OrdinalIgnoreCase);

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
            throw new InvalidOperationException(
                "DeepSeek models do not support image uploads.");

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
            thinking = new { type = "disabled" },
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
        var client = httpClientFactory.CreateClient("deepseek");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "v1/chat/completions")
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
            $"DeepSeek request failed with status {(int)response.StatusCode}.");
    }
}
