using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NinjaTrader_Xen.Endpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinjaTrader_Xen.Services;
using Xunit;

namespace NinjaTrader_Xen.Tests;

public sealed class TurnstileTests
{
    [Theory]
    [InlineData("{\"success\":true,\"hostname\":\"ninja.clickalgo.com\",\"action\":\"register\"}", true)]
    [InlineData("{\"success\":false,\"hostname\":\"meta.clickalgo.com\",\"action\":\"register\"}", false)]
    [InlineData("{\"success\":true,\"hostname\":\"meta.clickalgo.com\",\"action\":\"register\"}", false)]
    [InlineData("{\"success\":true,\"hostname\":\"ninja.clickalgo.com\",\"action\":\"login\"}", false)]
    [InlineData("{\"success\":true}", false)]
    [InlineData("not-json", false)]
    [InlineData("null", false)]
    public async Task RequiresSuccessfulRegistrationVerificationForThisHostname(string body, bool expected)
    {
        using var handler = new VerificationHandler(body);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://challenges.cloudflare.com/turnstile/v0/") };
        var service = CreateService(client);
        Assert.Equal(expected, await service.VerifyAsync("test-token", "192.0.2.10", default));
        Assert.Equal("/turnstile/v0/siteverify", handler.Path);
        Assert.Contains("response=test-token", handler.RequestBody);
        Assert.Contains("remoteip=192.0.2.10", handler.RequestBody);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task MissingTokenDoesNotCallCloudflare(string? token)
    {
        using var handler = new VerificationHandler("{}");
        using var client = new HttpClient(handler);
        Assert.False(await CreateService(client).VerifyAsync(token, null, default));
        Assert.Null(handler.Path);
    }

    [Fact]
    public async Task MissingSecretBlocksRegistrationWithoutCallingCloudflare()
    {
        using var handler = new VerificationHandler("{}");
        using var client = new HttpClient(handler);
        Assert.False(await CreateService(client, "").VerifyAsync("test-token", null, default));
        Assert.Null(handler.Path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloudflareErrorsBlockVerification(bool timeout)
    {
        using var handler = new VerificationHandler("{}", timeout ? new TaskCanceledException() : new HttpRequestException());
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://challenges.cloudflare.com/turnstile/v0/") };
        Assert.False(await CreateService(client).VerifyAsync("test-token", null, default));
    }

    [Theory]
    [InlineData(null, "{\"success\":true}", 200)]
    [InlineData("test-token", "{\"success\":false,\"error-codes\":[\"timeout-or-duplicate\"]}", 200)]
    [InlineData("test-token", "{\"success\":true,\"hostname\":\"wrong.example\",\"action\":\"register\"}", 200)]
    [InlineData("test-token", "{\"success\":true,\"hostname\":\"ninja.clickalgo.com\",\"action\":\"login\"}", 200)]
    [InlineData("test-token", "{}", 503)]
    public async Task RegistrationWithoutTokenIsRejectedBeforeDatabaseOrEmailWork(string? token, string body, int status)
    {
        using var verificationClient = new HttpClient(new VerificationHandler(body, status: (HttpStatusCode)status)) { BaseAddress = new Uri("https://challenges.cloudflare.com/turnstile/v0/") };
        await using var app = await RegistrationRateLimitTests.StartApp(services =>
        {
            services.AddSingleton<IHttpClientFactory>(new ClientFactory(verificationClient));
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Turnstile:SecretKey"] = "test-secret", ["Turnstile:Hostname"] = "ninja.clickalgo.com"
            }).Build());
        });
        using var client = RegistrationRateLimitTests.CreateClient(app);
        using var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email = "person@example.com", password = "test-password-123", turnstileToken = token });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Please complete the security check", await response.Content.ReadAsStringAsync());
    }

    private static TurnstileService CreateService(HttpClient client, string secret = "test-secret") => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Turnstile:SecretKey"] = secret,
            ["Turnstile:Hostname"] = "ninja.clickalgo.com"
        }).Build(), new ClientFactory(client), NullLogger<TurnstileService>.Instance);

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class VerificationHandler(string body, Exception? failure = null, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? RequestBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (failure is not null)
                throw failure;
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
