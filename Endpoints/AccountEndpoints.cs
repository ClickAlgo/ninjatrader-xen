using Microsoft.Data.SqlClient;
using NinjaTrader_Xen.Options;
using System.Data;
using System.Security.Claims;

namespace NinjaTrader_Xen.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account").RequireAuthorization();
        group.MapGet("/summary", Summary);
        group.MapGet("/transactions", Transactions);
        return app;
    }

    private static async Task<IResult> Summary(
        HttpContext context,
        IConfiguration configuration)
    {
        if (!TryGetSubscriberId(context.User, out var subscriberId))
            return Results.Unauthorized();

        await using var connection = new SqlConnection(RequiredConnectionString(configuration));
        await connection.OpenAsync();

        if (!await IsNinjaTraderSubscriber(connection, subscriberId))
            return Results.Forbid();

        decimal balanceGbp = 0;
        await using (var balanceCommand = new SqlCommand(
            "dbo.GetSubscriberCreditBalanceGbp",
            connection)
        {
            CommandType = CommandType.StoredProcedure
        })
        {
            balanceCommand.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
            var value = await balanceCommand.ExecuteScalarAsync();
            if (value is not null and not DBNull)
                balanceGbp = Convert.ToDecimal(value);
        }

        var entitlements = new List<object>();
        await using (var entitlementCommand = new SqlCommand("""
            SELECT CreditType, MAX(ExpiresUtc) AS ExpiresUtc
            FROM dbo.CreditPurchases
            WHERE SubscriberId = @SubscriberId
              AND ExpiresUtc >= SYSUTCDATETIME()
              AND CreditType IN ('Trial', 'Premium')
            GROUP BY CreditType;
            """, connection))
        {
            entitlementCommand.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
            await using var reader = await entitlementCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entitlements.Add(new
                {
                    type = reader.GetString(reader.GetOrdinal("CreditType")),
                    expiresUtc = reader.IsDBNull(reader.GetOrdinal("ExpiresUtc"))
                        ? (DateTime?)null
                        : reader.GetDateTime(reader.GetOrdinal("ExpiresUtc"))
                });
            }
        }

        return Results.Ok(new
        {
            subscriberId,
            platformId = PlatformIds.NinjaTrader,
            balanceGbp,
            entitlements
        });
    }

    private static async Task<IResult> Transactions(
        HttpContext context,
        IConfiguration configuration,
        int skip = 0,
        int take = 10)
    {
        if (!TryGetSubscriberId(context.User, out var subscriberId))
            return Results.Unauthorized();

        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 50);

        await using var connection = new SqlConnection(RequiredConnectionString(configuration));
        await connection.OpenAsync();

        if (!await IsNinjaTraderSubscriber(connection, subscriberId))
            return Results.Forbid();

        var transactions = new List<object>();
        await using var command = new SqlCommand("""
            SELECT PurchaseId, OrderId, AmountPaid, CreditType, CreatedUtc, ExpiresUtc
            FROM dbo.CreditPurchases
            WHERE SubscriberId = @SubscriberId
            ORDER BY CreatedUtc DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """, connection);
        command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
        command.Parameters.Add("@Skip", SqlDbType.Int).Value = skip;
        command.Parameters.Add("@Take", SqlDbType.Int).Value = take;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            transactions.Add(new
            {
                orderId = reader.GetString(reader.GetOrdinal("OrderId")),
                amountPaid = reader.GetDecimal(reader.GetOrdinal("AmountPaid")),
                creditType = reader.GetString(reader.GetOrdinal("CreditType")),
                createdUtc = reader.GetDateTime(reader.GetOrdinal("CreatedUtc")),
                expiresUtc = reader.IsDBNull(reader.GetOrdinal("ExpiresUtc"))
                    ? (DateTime?)null
                    : reader.GetDateTime(reader.GetOrdinal("ExpiresUtc"))
            });
        }

        return Results.Ok(new { transactions, skip, take });
    }

    private static async Task<bool> IsNinjaTraderSubscriber(
        SqlConnection connection,
        int subscriberId)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.Subscribers
            WHERE SubscriberId = @SubscriberId
              AND PlatformId = @PlatformId
              AND EmailVerified = 1
              AND Status = 1;
            """, connection);
        command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
        command.Parameters.Add("@PlatformId", SqlDbType.Int).Value = PlatformIds.NinjaTrader;
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static bool TryGetSubscriberId(ClaimsPrincipal user, out int subscriberId) =>
        int.TryParse(user.FindFirstValue("sid"), out subscriberId) && subscriberId > 0;

    private static string RequiredConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString("CodePilot")
        ?? throw new InvalidOperationException("ConnectionStrings:CodePilot is not configured.");
}
