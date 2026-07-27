using NinjaTrader_Xen.Models;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace NinjaTrader_Xen.Services;

public sealed class OpenAiStreamingClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IAiStreamingProvider
{
    public string DisplayName => "OpenAI";
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(configuration["OpenAI:ApiKey"]);

    public bool Supports(string model) =>
        model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase);

    public async IAsyncEnumerable<AiStreamEvent> StreamAsync(
        string model,
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string prompt,
        int maximumOutputTokens,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var input = new List<object>
        {
            new
            {
                role = "developer",
                content = new[] { new { type = "input_text", text = systemPrompt } }
            }
        };

        foreach (var turn in history)
        {
            input.Add(new
            {
                role = turn.Role.ToLowerInvariant(),
                content = new[]
                {
                    new
                    {
                        type = turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                            ? "output_text"
                            : "input_text",
                        text = turn.Content
                    }
                }
            });
        }

        input.Add(new
        {
            role = "user",
            content = new[] { new { type = "input_text", text = prompt } }
        });

        var body = new
        {
            model,
            stream = true,
            max_output_tokens = maximumOutputTokens,
            input
        };

        using var response = await SendAsync(body, cancellationToken);
        await foreach (var payload in StreamingJson.ReadSse(
            response,
            cancellationToken))
        {
            if (!payload.TryGetProperty("type", out var typeProperty))
                continue;

            var type = typeProperty.GetString();
            if (type == "response.output_text.delta" &&
                payload.TryGetProperty("delta", out var deltaProperty))
            {
                var delta = deltaProperty.GetString();
                if (!string.IsNullOrEmpty(delta))
                    yield return new AiStreamEvent(delta, 0, 0, false);
            }
            else if (type == "response.completed" &&
                     payload.TryGetProperty("response", out var completed) &&
                     completed.TryGetProperty("usage", out var usage))
            {
                yield return new AiStreamEvent(
                    null,
                    StreamingJson.ReadInt(usage, "input_tokens"),
                    StreamingJson.ReadInt(usage, "output_tokens"),
                    true);
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        object body,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("openai");
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/responses")
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
            $"OpenAI request failed with status {(int)response.StatusCode}.");
    }
}
