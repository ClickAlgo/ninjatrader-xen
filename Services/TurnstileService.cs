using System.Text.Json;

namespace NinjaTrader_Xen.Services;

public sealed class TurnstileService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<TurnstileService> logger)
{
    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
            return false;

        var secret = configuration["Turnstile:SecretKey"];
        var hostname = configuration["Turnstile:Hostname"];
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(hostname))
        {
            logger.LogWarning("Turnstile registration verification is not configured.");
            return false;
        }

        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = secret,
                ["response"] = token,
                ["remoteip"] = remoteIp ?? ""
            });
            using var response = await httpClientFactory.CreateClient("turnstile")
                .PostAsync("siteverify", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Turnstile verification returned HTTP {StatusCode}.", (int)response.StatusCode);
                return false;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var result = document.RootElement;
            return result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True
                && result.TryGetProperty("hostname", out var host) && host.ValueKind == JsonValueKind.String
                && string.Equals(host.GetString(), hostname, StringComparison.OrdinalIgnoreCase)
                && result.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.String
                && action.GetString() == "register";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            logger.LogWarning("Turnstile verification could not complete ({ErrorType}).", exception.GetType().Name);
            return false;
        }
    }
}
