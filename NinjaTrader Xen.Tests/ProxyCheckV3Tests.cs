using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinjaTrader_Xen.Services;

namespace NinjaTrader_Xen.Tests;

public sealed class ProxyCheckV3Tests
{
    [Theory]
    [InlineData("proxy")]
    [InlineData("vpn")]
    [InlineData("tor")]
    [InlineData("hosting")]
    [InlineData("anonymous")]
    [InlineData("compromised")]
    [InlineData("scraper")]
    public async Task PositiveDetectionBlocksEvenWithLowRisk(string flag)
    {
        Assert.True(await Check(new() { [flag] = true, ["risk"] = 0 }));
    }

    [Theory]
    [InlineData(0, "Business", "US", false)]
    [InlineData(49, "Residential", "US", false)]
    [InlineData(50, "Business", "US", true)]
    [InlineData(100, "Business", "US", true)]
    [InlineData(0, "Hosting", "US", true)]
    [InlineData(0, "Business", "NG", true)]
    public async Task NestedRiskCountryAndNetworkRules(int risk, string type, string country, bool expected)
    {
        Assert.Equal(expected, await Check(new() { ["proxy"] = false, ["risk"] = risk }, type, country));
    }

    [Fact]
    public async Task HttpFailurePreservesExistingFailOpenPolicy()
    {
        Assert.False(await Check(new(), httpStatus: HttpStatusCode.ServiceUnavailable));
    }

    private static async Task<bool> Check(Dictionary<string, object> detections,
        string type = "Business", string country = "US", HttpStatusCode httpStatus = HttpStatusCode.OK)
    {
        // Metadata deliberately precedes the IP record, exercising address selection.
        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["status"] = "ok",
            ["metadata"] = new { description = "not an address record" },
            ["104.255.98.77"] = new
            {
                detections,
                network = new { type },
                location = new { country_code = country }
            }
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProxyCheck:ApiKey"] = "test-key",
            ["ProxyCheck:RiskThreshold"] = "50"
        }).Build();
        using var client = new HttpClient(new ResponseHandler(body, httpStatus))
        {
            BaseAddress = new Uri("https://proxycheck.io/v3/")
        };
        var service = new FreeTrialService(config, new Factory(client), null!, NullLogger<FreeTrialService>.Instance);
        var method = typeof(FreeTrialService).GetMethod("CheckNetworkAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<(bool Blocked, string? CountryCode)>)method.Invoke(service,
            new object?[] { "104.255.98.77", new FreeTrialOptions { BlockedCountryCodes = ["NG"] }, CancellationToken.None })!;
        return (await task).Blocked;
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ResponseHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/v3/104.255.98.77/", request.RequestUri!.AbsolutePath);
            Assert.Equal("?key=test-key", request.RequestUri.Query);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
