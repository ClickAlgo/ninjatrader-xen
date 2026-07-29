using NinjaTrader_Xen.Models;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace NinjaTrader_Xen.Services;

public sealed class ClaudeStreamingClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IAiStreamingProvider
{
    public string DisplayName => "Claude";
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(configuration["Claude:ApiKey"]);

    public bool Supports(string model) =>
        model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase);

    public bool SupportsImages(string model) => Supports(model);

    public async IAsyncEnumerable<AiStreamEvent> StreamAsync(
        string model,
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string prompt,
        AiImage? image,
        int maximumOutputTokens,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var messages = history
            .Select(turn => new
            {
                role = turn.Role.ToLowerInvariant(),
                content = turn.Content
            })
            .Cast<object>()
            .ToList();
        var userContent = new List<object>
        {
            new { type = "text", text = prompt }
        };
        if (image is not null)
        {
            userContent.Add(new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = image.MediaType,
                    data = image.Base64Data
                }
            });
        }
        messages.Add(new { role = "user", content = userContent });

        var body = new
        {
            model,
            stream = true,
            max_tokens = maximumOutputTokens,
            system = systemPrompt,
            messages
        };

        using var response = await SendAsync(body, cancellationToken);
        var inputTokens = 0;
        var outputTokens = 0;

        await foreach (var payload in StreamingJson.ReadSse(
            response,
            cancellationToken))
        {
            if (!payload.TryGetProperty("type", out var typeProperty))
                continue;

            switch (typeProperty.GetString())
            {
                case "message_start"
                    when payload.TryGetProperty("message", out var message) &&
                         message.TryGetProperty("usage", out var startUsage):
                    inputTokens = StreamingJson.ReadInt(
                        startUsage,
                        "input_tokens");
                    break;

                case "content_block_delta"
                    when payload.TryGetProperty("delta", out var deltaObject) &&
                         deltaObject.TryGetProperty("text", out var textProperty):
                    var delta = textProperty.GetString();
                    if (!string.IsNullOrEmpty(delta))
                        yield return new AiStreamEvent(delta, 0, 0, false);
                    break;

                case "message_delta"
                    when payload.TryGetProperty("usage", out var deltaUsage):
                    outputTokens = StreamingJson.ReadInt(
                        deltaUsage,
                        "output_tokens");
                    break;

                case "message_stop":
                    yield return new AiStreamEvent(
                        null,
                        inputTokens,
                        outputTokens,
                        true);
                    break;
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        object body,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("claude");
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
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
            $"Claude request failed with status {(int)response.StatusCode}.");
    }
}
