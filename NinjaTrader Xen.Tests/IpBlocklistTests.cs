using System.Net;
using NinjaTrader_Xen.Security;

namespace NinjaTrader_Xen.Tests;

public sealed class IpBlocklistTests
{
    private static readonly string[] AbusiveSubnet = ["105.164.128.0/24"];

    [Theory]
    [InlineData("105.164.128.113")]
    [InlineData("105.164.128.147")]
    [InlineData("105.164.128.254")]
    public void ConfiguredIpv4CidrBlocksAddressesWithinSubnet(string address)
    {
        Assert.True(IpBlocklist.TryMatch(address, AbusiveSubnet, out var rule));
        Assert.Equal("105.164.128.0/24", rule);
    }

    [Fact]
    public void ConfiguredIpv4CidrAllowsAddressOutsideSubnet()
    {
        Assert.False(IpBlocklist.TryMatch(
            "105.164.129.1",
            AbusiveSubnet,
            out var rule));
        Assert.Null(rule);
    }

    [Fact]
    public void Ipv4MappedIpv6MatchesIpv4Cidr()
    {
        var address = IPAddress.Parse("::ffff:105.164.128.113");

        Assert.True(IpBlocklist.TryMatch(address, AbusiveSubnet, out var rule));
        Assert.Equal("105.164.128.0/24", rule);
    }

    [Theory]
    [InlineData("203.0.113.17", "203.0.113.17")]
    [InlineData("2001:db8::17", "2001:db8::17")]
    public void ExactIpRulesMatchOnlyTheConfiguredAddress(
        string address,
        string rule)
    {
        Assert.True(IpBlocklist.TryMatch(address, [rule], out var matched));
        Assert.Equal(rule, matched);
    }

    [Fact]
    public void Ipv6CidrMatchesOnlyAddressesWithinRange()
    {
        const string rule = "2001:db8:abcd::/48";

        Assert.True(IpBlocklist.TryMatch(
            "2001:db8:abcd:42::1",
            [rule],
            out var matched));
        Assert.Equal(rule, matched);
        Assert.False(IpBlocklist.TryMatch(
            "2001:db8:abce::1",
            [rule],
            out _));
    }

    [Theory]
    [InlineData("not-an-address", "105.164.128.0/24")]
    [InlineData("105.164.128.113", "not-a-rule")]
    [InlineData("105.164.128.113", "105.164.128.0/99")]
    [InlineData("2001:db8::1", "2001:db8::/999")]
    [InlineData("105.164.128.113", "")]
    public void MalformedAddressesAndRulesFailOpenWithoutBlocking(
        string address,
        string rule)
    {
        var exception = Record.Exception(() =>
            Assert.False(IpBlocklist.TryMatch(address, [rule], out _)));

        Assert.Null(exception);
    }
}
