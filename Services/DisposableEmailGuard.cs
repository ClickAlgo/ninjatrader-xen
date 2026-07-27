using System.Net;
using System.Text.Json;

namespace NinjaTrader_Xen.Services;

public sealed class DisposableEmailGuard(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<DisposableEmailGuard> logger)
{
    public async Task<bool> IsAllowedAsync(
        string email,
        CancellationToken cancellationToken)
    {
        var atIndex = email.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == email.Length - 1)
            return false;

        var domain = email[(atIndex + 1)..].Trim().ToLowerInvariant();
        var apiKey = configuration["RapidApi:MailCheckKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning(
                "RapidAPI MailCheck is not configured; disposable-email screening is unavailable.");
            return true;
        }

        try
        {
            var client = httpClientFactory.CreateClient("mail-check");
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"?domain={Uri.EscapeDataString(domain)}");
            request.Headers.Add("x-rapidapi-key", apiKey);
            request.Headers.Add(
                "x-rapidapi-host",
                "mailcheck.p.rapidapi.com");

            using var response = await client.SendAsync(
                request,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning(
                    "RapidAPI MailCheck quota was exceeded; registration is failing open.");
                return true;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "RapidAPI MailCheck returned status {StatusCode}; registration is failing open.",
                    (int)response.StatusCode);
                return true;
            }

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty(
                    "block",
                    out var blockProperty))
            {
                return true;
            }

            return blockProperty.ValueKind switch
            {
                JsonValueKind.True => false,
                JsonValueKind.False => true,
                JsonValueKind.String when
                    bool.TryParse(
                        blockProperty.GetString(),
                        out var block) => !block,
                _ => true
            };
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "RapidAPI MailCheck failed for domain {Domain}; registration is failing open.",
                domain);
            return true;
        }
    }
}
