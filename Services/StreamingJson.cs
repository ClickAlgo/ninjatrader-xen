using System.Runtime.CompilerServices;
using System.Text.Json;

namespace NinjaTrader_Xen.Services;

internal static class StreamingJson
{
    public static async IAsyncEnumerable<JsonElement> ReadSse(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        TimeSpan? idleTimeout = null)
    {
        await using var stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            if (idleTimeout.HasValue)
            {
                using var idleCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                idleCancellation.CancelAfter(idleTimeout.Value);
                try
                {
                    line = await reader.ReadLineAsync(
                        idleCancellation.Token);
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        "The AI provider stopped sending stream data.");
                }
            }
            else
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }

            if (line is null)
                yield break;

            if (string.IsNullOrWhiteSpace(line) ||
                !line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data == "[DONE]")
                yield break;

            JsonElement payload;
            try
            {
                payload = JsonSerializer.Deserialize<JsonElement>(data);
            }
            catch (JsonException)
            {
                continue;
            }

            yield return payload;
        }
    }

    public static int ReadInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
}
