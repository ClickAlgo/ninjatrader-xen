using Microsoft.Data.SqlClient;
using NinjaTrader_Xen.Options;
using NinjaTrader_Xen.Security;
using System.Data;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NinjaTrader_Xen.Services;

public sealed class FreeTrialOptions
{
    public bool Enabled { get; init; } = true;
    public decimal AmountGbp { get; init; } = 5m;
    public int DurationHours { get; init; } = 48;
    public string[] BlockedCountryCodes { get; init; } = [];
    public string[] BlockedIpRanges { get; init; } = [];
}

public sealed class ProxyCheckOptions
{
    public string ApiKey { get; init; } = "";
    public decimal RiskThreshold { get; init; } = 50m;
}

public sealed record FreeTrialResult(bool Granted, DateTime? ExpiresUtc);

public sealed class FreeTrialService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    AccountEmailSender emailSender,
    ILogger<FreeTrialService> logger) : IRegistrationNetworkMetadataRecorder
{
    public async Task RecordRegistrationNetworkAsync(
        int subscriberId,
        IPAddress? remoteIp,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await InspectNetworkAsync(
                remoteIp?.ToString(),
                cancellationToken);
            if (!result.Available)
                return;

            await using var connection = new SqlConnection(
                configuration.GetConnectionString("CodePilot") ??
                throw new InvalidOperationException(
                    "ConnectionStrings:CodePilot is not configured."));
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("""
                UPDATE dbo.Subscribers
                SET RegistrationConnectionType = @ConnectionType,
                    RegistrationCountryCode = @CountryCode,
                    UpdatedUtc = SYSUTCDATETIME()
                WHERE SubscriberId = @SubscriberId
                  AND PlatformId = @PlatformId;
                """, connection);
            command.Parameters.Add("@ConnectionType", SqlDbType.NVarChar, 32)
                .Value = result.ConnectionType;
            command.Parameters.Add("@CountryCode", SqlDbType.Char, 2)
                .Value = result.CountryCode is { Length: 2 }
                    ? result.CountryCode.ToUpperInvariant()
                    : DBNull.Value;
            command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
            command.Parameters.Add("@PlatformId", SqlDbType.Int).Value =
                PlatformIds.NinjaTrader;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Unable to record registration network metadata for subscriber {SubscriberId}.",
                subscriberId);
        }
    }

    public async Task<FreeTrialResult> GrantIfEligibleAsync(
        int subscriberId,
        string email,
        string? deviceFingerprint,
        IPAddress? remoteIp,
        CancellationToken cancellationToken)
    {
        var options = configuration
            .GetSection("FreeTrial")
            .Get<FreeTrialOptions>() ?? new FreeTrialOptions();

        if (!options.Enabled ||
            options.AmountGbp <= 0 ||
            options.DurationHours <= 0 ||
            string.IsNullOrWhiteSpace(deviceFingerprint) ||
            deviceFingerprint.Length is < 16 or > 256)
        {
            return new FreeTrialResult(false, null);
        }

        if (IpBlocklist.TryMatch(
                remoteIp,
                options.BlockedIpRanges,
                out var matchedRule))
        {
            logger.LogWarning(
                "Free-trial credit withheld because client IP {ClientIp} matched configured blocklist rule {BlockedIpRule}.",
                remoteIp,
                matchedRule);
            return new FreeTrialResult(false, null);
        }

        var networkCheck = await CheckNetworkAsync(
            remoteIp?.ToString(),
            options,
            cancellationToken);
        if (networkCheck.Blocked)
            return new FreeTrialResult(false, null);

        var deviceHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(deviceFingerprint)));

        try
        {
            var result = await GrantInTransaction(
                subscriberId,
                deviceHash,
                options,
                cancellationToken);

            if (result.Granted && result.ExpiresUtc.HasValue)
            {
                try
                {
                    await emailSender.SendFreeTrialActivatedAsync(
                        email,
                        options.AmountGbp,
                        result.ExpiresUtc.Value);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Unable to send free-trial email to subscriber {SubscriberId}.",
                        subscriberId);
                }
            }

            return result;
        }
        catch (SqlException exception)
        {
            logger.LogError(
                exception,
                "Unable to evaluate free-trial credit for subscriber {SubscriberId}.",
                subscriberId);
            return new FreeTrialResult(false, null);
        }
    }

    private async Task<FreeTrialResult> GrantInTransaction(
        int subscriberId,
        string deviceHash,
        FreeTrialOptions options,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(
            configuration.GetConnectionString("CodePilot") ??
            throw new InvalidOperationException(
                "ConnectionStrings:CodePilot is not configured."));
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)
            await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            string? registrationIp;
            bool trialGranted;

            await using (var subscriberCommand = new SqlCommand("""
                SELECT TrialGranted, RegistrationIp
                FROM dbo.Subscribers WITH (UPDLOCK, HOLDLOCK)
                WHERE SubscriberId = @SubscriberId
                  AND PlatformId = @PlatformId
                  AND EmailVerified = 1
                  AND Status = 1;
                """, connection, transaction))
            {
                subscriberCommand.Parameters.Add(
                    "@SubscriberId",
                    SqlDbType.Int).Value = subscriberId;
                subscriberCommand.Parameters.Add(
                    "@PlatformId",
                    SqlDbType.Int).Value = PlatformIds.NinjaTrader;

                await using var reader =
                    await subscriberCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new FreeTrialResult(false, null);
                }

                trialGranted = reader.GetBoolean(
                    reader.GetOrdinal("TrialGranted"));
                registrationIp = reader.IsDBNull(
                    reader.GetOrdinal("RegistrationIp"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("RegistrationIp"));
            }

            if (trialGranted ||
                await HasExistingTrial(
                    connection,
                    transaction,
                    subscriberId,
                    deviceHash,
                    cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new FreeTrialResult(false, null);
            }

            await using (var duplicateCommand = new SqlCommand("""
                SELECT COUNT(1)
                FROM dbo.Subscribers WITH (UPDLOCK, HOLDLOCK)
                WHERE PlatformId = @PlatformId
                  AND SubscriberId <> @SubscriberId
                  AND TrialGranted = 1
                  AND
                  (
                      DeviceFingerprintHash = @DeviceHash
                      OR
                      (
                          @RegistrationIp IS NOT NULL
                          AND RegistrationIp = @RegistrationIp
                      )
                  );
                """, connection, transaction))
            {
                duplicateCommand.Parameters.Add(
                    "@PlatformId",
                    SqlDbType.Int).Value = PlatformIds.NinjaTrader;
                duplicateCommand.Parameters.Add(
                    "@SubscriberId",
                    SqlDbType.Int).Value = subscriberId;
                duplicateCommand.Parameters.Add(
                    "@DeviceHash",
                    SqlDbType.Char,
                    64).Value = deviceHash;
                duplicateCommand.Parameters.Add(
                    "@RegistrationIp",
                    SqlDbType.VarChar,
                    45).Value =
                        IsUsableIp(registrationIp)
                            ? registrationIp!
                            : DBNull.Value;

                if (Convert.ToInt32(
                        await duplicateCommand.ExecuteScalarAsync(
                            cancellationToken)) > 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new FreeTrialResult(false, null);
                }
            }

            await using (var grantCommand = new SqlCommand("""
                INSERT INTO dbo.FreeTrialGrants
                (
                    SubscriberId,
                    DeviceHash,
                    IpPrefix,
                    GrantedUtc
                )
                VALUES
                (
                    @SubscriberId,
                    @DeviceHash,
                    @IpPrefix,
                    SYSUTCDATETIME()
                );
                """, connection, transaction))
            {
                grantCommand.Parameters.Add(
                    "@SubscriberId",
                    SqlDbType.Int).Value = subscriberId;
                grantCommand.Parameters.Add(
                    "@DeviceHash",
                    SqlDbType.NVarChar,
                    128).Value = deviceHash;
                grantCommand.Parameters.Add(
                    "@IpPrefix",
                    SqlDbType.NVarChar,
                    128).Value =
                        CreateIpPrefix(registrationIp) is { } ipPrefix
                            ? ipPrefix
                            : DBNull.Value;
                await grantCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            DateTime expiresUtc;
            await using (var insertCommand = new SqlCommand("""
                INSERT INTO dbo.CreditPurchases
                (
                    SubscriberId,
                    OrderId,
                    AmountPaid,
                    CreditRemainingGbp,
                    CreatedUtc,
                    CreditType,
                    ExpiresUtc
                )
                OUTPUT INSERTED.ExpiresUtc
                VALUES
                (
                    @SubscriberId,
                    @OrderId,
                    @AmountGbp,
                    @AmountGbp,
                    SYSUTCDATETIME(),
                    'Trial',
                    DATEADD(hour, @DurationHours, SYSUTCDATETIME())
                );
                """, connection, transaction))
            {
                insertCommand.Parameters.Add(
                    "@SubscriberId",
                    SqlDbType.Int).Value = subscriberId;
                insertCommand.Parameters.Add(
                    "@OrderId",
                    SqlDbType.NVarChar,
                    255).Value = $"TRIAL-P{PlatformIds.NinjaTrader}-{subscriberId}";
                var amount = insertCommand.Parameters.Add(
                    "@AmountGbp",
                    SqlDbType.Decimal);
                amount.Precision = 10;
                amount.Scale = 2;
                amount.Value = options.AmountGbp;
                insertCommand.Parameters.Add(
                    "@DurationHours",
                    SqlDbType.Int).Value = options.DurationHours;
                expiresUtc = Convert.ToDateTime(
                    await insertCommand.ExecuteScalarAsync(cancellationToken));
            }

            await using (var markCommand = new SqlCommand("""
                UPDATE dbo.Subscribers
                SET TrialGranted = 1,
                    DeviceFingerprintHash =
                        COALESCE(DeviceFingerprintHash, @DeviceHash),
                    UpdatedUtc = SYSUTCDATETIME()
                WHERE SubscriberId = @SubscriberId
                  AND PlatformId = @PlatformId;
                """, connection, transaction))
            {
                markCommand.Parameters.Add(
                    "@DeviceHash",
                    SqlDbType.Char,
                    64).Value = deviceHash;
                markCommand.Parameters.Add(
                    "@SubscriberId",
                    SqlDbType.Int).Value = subscriberId;
                markCommand.Parameters.Add(
                    "@PlatformId",
                    SqlDbType.Int).Value = PlatformIds.NinjaTrader;
                await markCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new FreeTrialResult(true, expiresUtc);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<bool> HasExistingTrial(
        SqlConnection connection,
        SqlTransaction transaction,
        int subscriberId,
        string deviceHash,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT
                CASE
                    WHEN EXISTS
                    (
                        SELECT 1
                        FROM dbo.CreditPurchases WITH (UPDLOCK, HOLDLOCK)
                        WHERE SubscriberId = @SubscriberId
                          AND CreditType = 'Trial'
                    )
                    OR EXISTS
                    (
                        SELECT 1
                        FROM dbo.FreeTrialGrants WITH (UPDLOCK, HOLDLOCK)
                        WHERE SubscriberId = @SubscriberId
                           OR DeviceHash = @DeviceHash
                    )
                    THEN 1
                    ELSE 0
                END;
            """, connection, transaction);
        command.Parameters.Add(
            "@SubscriberId",
            SqlDbType.Int).Value = subscriberId;
        command.Parameters.Add(
            "@DeviceHash",
            SqlDbType.NVarChar,
            128).Value = deviceHash;
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private async Task<(bool Blocked, string? CountryCode)> CheckNetworkAsync(
        string? rawIp,
        FreeTrialOptions trialOptions,
        CancellationToken cancellationToken)
    {
        var result = await InspectNetworkAsync(rawIp, cancellationToken);
        if (!result.Available)
            return (false, null);

        var proxyOptions = configuration
            .GetSection("ProxyCheck")
            .Get<ProxyCheckOptions>() ?? new ProxyCheckOptions();
        var blocked = result.ConnectionType is "Proxy" or "Hosting" ||
            result.Risk >= proxyOptions.RiskThreshold ||
            trialOptions.BlockedCountryCodes.Any(code =>
                string.Equals(code, result.CountryCode, StringComparison.OrdinalIgnoreCase));
        return (blocked, result.CountryCode);
    }

    private async Task<(bool Available, string ConnectionType, string? CountryCode, decimal Risk)> InspectNetworkAsync(
        string? rawIp,
        CancellationToken cancellationToken)
    {
        var proxyOptions = configuration
            .GetSection("ProxyCheck")
            .Get<ProxyCheckOptions>() ?? new ProxyCheckOptions();
        if (string.IsNullOrWhiteSpace(proxyOptions.ApiKey))
        {
            logger.LogWarning(
                "ProxyCheck is not configured; free-trial network screening is unavailable.");
            return (false, "Unknown", null, 0);
        }

        var ip = SanitizeIp(rawIp);
        var useCallerIp = string.IsNullOrWhiteSpace(ip) || IsPrivateOrLocalIp(ip);
        var address = useCallerIp
            ? $"?key={Uri.EscapeDataString(proxyOptions.ApiKey)}"
            : $"{Uri.EscapeDataString(ip!)}/?key={Uri.EscapeDataString(proxyOptions.ApiKey)}";

        try
        {
            var client = httpClientFactory.CreateClient("proxy-check");
            using var response = await client.GetAsync(address, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "ProxyCheck returned status {StatusCode}. Failing open.",
                    (int)response.StatusCode);
                return (false, "Unknown", null, 0);
            }

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            var record = FindAddressRecord(document.RootElement, useCallerIp ? null : ip);
            if (!record.HasValue)
                return (false, "Unknown", null, 0);

            var detections = record.Value.GetProperty("detections");
            var countryCode = ReadString(record.Value.GetProperty("location"), "country_code");
            var type = ReadString(record.Value.GetProperty("network"), "type") ?? "";
            var risk = ReadDecimal(detections, "risk");
            var detectedProxy =
                ReadBoolean(detections, "proxy") ||
                ReadBoolean(detections, "vpn") ||
                ReadBoolean(detections, "tor") ||
                ReadBoolean(detections, "anonymous") ||
                ReadBoolean(detections, "compromised") ||
                ReadBoolean(detections, "scraper");
            var detectedHosting =
                ReadBoolean(detections, "hosting") ||
                type.Contains("HOSTING", StringComparison.OrdinalIgnoreCase);
            var connectionType = detectedProxy
                ? "Proxy"
                : detectedHosting
                    ? "Hosting"
                    : string.IsNullOrWhiteSpace(type)
                        ? "Unknown"
                        : type;

            return (true, connectionType, countryCode, risk);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "ProxyCheck failed during free-trial screening. Failing open.");
            return (false, "Unknown", null, 0);
        }
    }

    private static JsonElement? FindAddressRecord(JsonElement root, string? requestedIp)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var status = ReadString(root, "status");
        if (status is not ("ok" or "warning"))
            return null;

        var address = requestedIp ?? ReadString(root, "ip");
        if (!IPAddress.TryParse(address, out var expectedIp))
            return null;

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object &&
                IPAddress.TryParse(property.Name, out var responseIp) &&
                responseIp.Equals(expectedIp))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ToString()
            : null;

    private static bool ReadBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static decimal ReadDecimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        decimal.TryParse(value.ToString(), out var result)
            ? result
            : 0;

    private static string? SanitizeIp(string? rawIp)
    {
        if (string.IsNullOrWhiteSpace(rawIp))
            return null;

        var value = rawIp.Trim();
        var scopeIndex = value.IndexOf('%');
        return scopeIndex >= 0 ? value[..scopeIndex] : value;
    }

    private static bool IsPrivateOrLocalIp(string ipValue)
    {
        if (!IPAddress.TryParse(ipValue, out var ip))
            return true;
        if (IPAddress.IsLoopback(ip))
            return true;

        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   bytes[0] == 169 && bytes[1] == 254 ||
                   bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                   bytes[0] == 192 && bytes[1] == 168;
        }

        return ip.AddressFamily == AddressFamily.InterNetworkV6 &&
               ((bytes[0] & 0xFE) == 0xFC ||
                bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);
    }

    private static bool IsUsableIp(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase);

    private static string? CreateIpPrefix(string? value)
    {
        var sanitized = SanitizeIp(value);
        if (string.IsNullOrWhiteSpace(sanitized) ||
            !IPAddress.TryParse(sanitized, out var address))
        {
            return null;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            Array.Clear(bytes, 8, 8);
            return $"{new IPAddress(bytes)}/64";
        }

        return null;
    }
}
