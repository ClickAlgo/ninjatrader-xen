using NinjaTrader_Xen.Services;

namespace NinjaTrader_Xen.Tests;

public sealed class StoredRegistrationTrialDecisionTests
{
    private static readonly DateTime CheckedUtc = new(2026, 9, 26, 11, 33, 40, DateTimeKind.Utc);

    [Theory]
    [InlineData("Hosting")]
    [InlineData("Proxy")]
    [InlineData("VPN")]
    [InlineData("TOR")]
    [InlineData("Corporate Hosting Network")]
    public void ProxyLikeRegistrationTypesAreDenied(string connectionType)
    {
        Assert.Equal("RegistrationNetworkBlocked", Decide(connectionType));
    }

    [Theory]
    [InlineData("Residential")]
    [InlineData("Business")]
    [InlineData("Wireless")]
    public void OrdinaryRegistrationTypesRemainEligible(string connectionType)
    {
        Assert.Null(Decide(connectionType));
    }

    [Fact]
    public void MissingRegistrationClassificationIsDenied()
    {
        Assert.Equal(
            "RegistrationNetworkUnavailable",
            Decide(null, checkedUtc: null, risk: null));
    }

    [Fact]
    public void MissingRegistrationIpIsDenied()
    {
        Assert.Equal(
            "RegistrationNetworkUnavailable",
            Decide("Residential", ip: "unknown"));
    }

    [Fact]
    public void RiskAtThresholdIsDenied()
    {
        Assert.Equal("RegistrationNetworkRisk", Decide("Business", risk: 50));
    }

    [Fact]
    public void BlockedRegistrationCountryIsDenied()
    {
        Assert.Equal("RegistrationCountryBlocked", Decide("Residential", country: "NG"));
    }

    [Fact]
    public void ConfiguredRegistrationIpRangeIsDenied()
    {
        Assert.Equal(
            "RegistrationIpBlocked",
            Decide("Residential", ip: "105.164.128.10"));
    }

    private static string? Decide(
        string? connectionType,
        string country = "GB",
        decimal? risk = 0,
        DateTime? checkedUtc = null,
        string ip = "198.51.100.10") =>
        FreeTrialService.GetRegistrationNetworkDenialReason(
            ip,
            connectionType,
            country,
            risk,
            checkedUtc ?? (connectionType is null ? null : CheckedUtc),
            new FreeTrialOptions
            {
                BlockedCountryCodes = ["NG"],
                BlockedIpRanges = ["105.164.128.0/24"]
            },
            new ProxyCheckOptions { RiskThreshold = 50 });
}
