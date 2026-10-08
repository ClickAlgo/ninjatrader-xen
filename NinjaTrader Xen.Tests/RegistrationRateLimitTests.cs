using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using NinjaTrader_Xen.Endpoints;
using NinjaTrader_Xen.Services;
using Xunit;

namespace NinjaTrader_Xen.Tests;

public sealed class RegistrationRateLimitTests
{
    [Fact]
    public async Task FourthInvalidAttemptIsBlockedWithExactMessage()
    {
        await using var app = await StartApp();
        using var client = CreateClient(app);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await Register(client, "192.0.2.10");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var blocked = await Register(client, "192.0.2.10");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        using var body = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        Assert.Equal("Too many registration attempts. Please try again in 15 minutes.",
            body.RootElement.GetProperty("message").GetString());

        using var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "", password = "" });
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
    }

    [Fact]
    public async Task AnotherIpHasItsOwnThreeAttemptAllowance()
    {
        await using var app = await StartApp();
        using var client = CreateClient(app);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var first = await Register(client, "192.0.2.10");
            using var second = await Register(client, "192.0.2.11");
            Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        }
        using var firstBlocked = await Register(client, "192.0.2.10");
        using var secondBlocked = await Register(client, "192.0.2.11");
        Assert.Equal(HttpStatusCode.TooManyRequests, firstBlocked.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondBlocked.StatusCode);
    }

    [Fact]
    public async Task SpoofedForwardedHeadersAndMappedIpv6DoNotBypassLimit()
    {
        await using var app = await StartApp();
        using var client = CreateClient(app);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await Register(client, "192.0.2.10", $"198.51.100.{attempt + 1}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var blocked = await Register(client, "::ffff:192.0.2.10", "198.51.100.99");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    internal static async Task<WebApplication> StartApp(Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        // Exercise the production policy and actual auth routes without new packages,
        // database access, mail delivery, or external services.
        builder.Services.AddRateLimiter(Program.ConfigureRegistrationRateLimiter);
        builder.Services.AddAuthorization();
        builder.Services.AddHttpClient();
        builder.Services.AddScoped<AccountEmailSender>();
        builder.Services.AddScoped<DisposableEmailGuard>();
        builder.Services.AddScoped<TurnstileService>();
        builder.Services.AddScoped<FreeTrialService>();
        builder.Services.AddSingleton<IRegistrationNetworkMetadataQueue, RegistrationNetworkMetadataQueue>();
        configure?.Invoke(builder.Services);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            // Only the test host accepts this header to simulate the connection IP.
            context.Connection.RemoteIpAddress = IPAddress.Parse(
                context.Request.Headers["X-Test-Connection-IP"].FirstOrDefault() ?? "192.0.2.10");
            await next();
        });
        app.UseRouting();
        app.UseRateLimiter();
        app.MapAuthEndpoints();
        await app.StartAsync();
        return app;
    }

    internal static HttpClient CreateClient(WebApplication app) => new()
    {
        BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single())
    };

    private static async Task<HttpResponseMessage> Register(
        HttpClient client, string ip, string? forwardedIp = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { email = "invalid", password = "test-password-123" })
        };
        request.Headers.Add("X-Test-Connection-IP", ip);
        if (forwardedIp is not null)
            request.Headers.Add("X-Forwarded-For", forwardedIp);
        return await client.SendAsync(request);
    }
}
