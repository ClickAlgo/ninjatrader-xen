using System.Net;
using System.Net.Sockets;

namespace NinjaTrader_Xen.Security;

public static class IpBlocklist
{
    public static bool TryMatch(
        IPAddress? address,
        IEnumerable<string>? configuredRules,
        out string? matchedRule)
    {
        matchedRule = null;
        if (address is null || configuredRules is null)
            return false;

        var normalizedAddress = Normalize(address, out _);
        foreach (var configuredRule in configuredRules)
        {
            if (!TryParseRule(configuredRule, out var network, out var prefixLength))
                continue;

            if (!IsInRange(normalizedAddress, network!, prefixLength))
                continue;

            matchedRule = configuredRule.Trim();
            return true;
        }

        return false;
    }

    public static bool TryMatch(
        string? address,
        IEnumerable<string>? configuredRules,
        out string? matchedRule)
    {
        matchedRule = null;
        return IPAddress.TryParse(address, out var parsed) &&
               TryMatch(parsed, configuredRules, out matchedRule);
    }

    private static bool TryParseRule(
        string? configuredRule,
        out IPAddress? network,
        out int prefixLength)
    {
        network = null;
        prefixLength = 0;
        if (string.IsNullOrWhiteSpace(configuredRule))
            return false;

        var parts = configuredRule.Trim().Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out var parsedAddress))
            return false;

        var wasMapped = parsedAddress.IsIPv4MappedToIPv6;
        network = Normalize(parsedAddress, out var maximumPrefixLength);
        prefixLength = maximumPrefixLength;

        if (parts.Length == 1)
            return true;

        if (!int.TryParse(parts[1], out var configuredPrefix))
            return false;

        if (wasMapped)
        {
            if (configuredPrefix is < 96 or > 128)
                return false;
            configuredPrefix -= 96;
        }

        if (configuredPrefix < 0 || configuredPrefix > maximumPrefixLength)
            return false;

        prefixLength = configuredPrefix;
        return true;
    }

    private static IPAddress Normalize(
        IPAddress address,
        out int maximumPrefixLength)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        maximumPrefixLength = address.AddressFamily switch
        {
            AddressFamily.InterNetwork => 32,
            AddressFamily.InterNetworkV6 => 128,
            _ => 0
        };
        return address;
    }

    private static bool IsInRange(
        IPAddress address,
        IPAddress network,
        int prefixLength)
    {
        if (address.AddressFamily != network.AddressFamily)
            return false;

        var addressBytes = address.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();
        var wholeBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var index = 0; index < wholeBytes; index++)
        {
            if (addressBytes[index] != networkBytes[index])
                return false;
        }

        if (remainingBits == 0)
            return true;

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (addressBytes[wholeBytes] & mask) ==
               (networkBytes[wholeBytes] & mask);
    }
}
