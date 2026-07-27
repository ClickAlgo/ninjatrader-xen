using Microsoft.Data.SqlClient;
using NinjaTrader_Xen.Models;
using NinjaTrader_Xen.Options;
using Stripe;
using Stripe.Checkout;
using System.Data;
using System.Globalization;
using System.Security.Claims;

namespace NinjaTrader_Xen.Endpoints;

public static class StripeEndpoints
{
    private static readonly HashSet<int> AllowedTopUps = [1, 5, 10, 15, 25];

    public static IEndpointRouteBuilder MapStripeEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/payments/create-checkout", CreateCheckout)
            .RequireAuthorization();
        app.MapPost("/api/stripe/webhook", Webhook);
        return app;
    }

    private static async Task<IResult> CreateCheckout(
        CreateCheckoutRequest request,
        HttpContext context,
        IConfiguration configuration,
        ILogger<Program> logger)
    {
        if (!int.TryParse(
                context.User.FindFirstValue("sid"),
                out var subscriberId) ||
            subscriberId <= 0)
        {
            return Results.Unauthorized();
        }

        if (!AllowedTopUps.Contains(request.AmountGbp))
        {
            return Results.BadRequest(new
            {
                message = "Select a valid top-up amount."
            });
        }

        if (!await IsEligibleNinjaTraderSubscriber(
                configuration,
                subscriberId))
        {
            return Results.Forbid();
        }

        var secretKey = configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            logger.LogError("Stripe:SecretKey is not configured.");
            return Results.Problem(
                "The payment service is not configured.");
        }

        var baseUrl = configuration["App:BaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return Results.Problem("App:BaseUrl is not configured.");
        }

        var subscriber = subscriberId.ToString(CultureInfo.InvariantCulture);
        var amount = request.AmountGbp.ToString(CultureInfo.InvariantCulture);
        const string description = "NinjaTrader Xen AI Credit";
        var metadata = new Dictionary<string, string>
        {
            ["subscriberId"] = subscriber,
            ["platformId"] =
                PlatformIds.NinjaTrader.ToString(CultureInfo.InvariantCulture),
            ["amountGbp"] = amount,
            ["product"] = description
        };

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            AutomaticTax = new SessionAutomaticTaxOptions
            {
                Enabled = true
            },
            ClientReferenceId = subscriber,
            CustomerEmail = context.User.FindFirstValue(ClaimTypes.Email),
            Metadata = metadata,
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                Description = description,
                Metadata = new Dictionary<string, string>(metadata)
            },
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "gbp",
                        UnitAmount = request.AmountGbp * 100L,
                        TaxBehavior = "exclusive",
                        ProductData =
                            new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = description,
                                Description =
                                    "Prepaid AI usage credit for NinjaTrader Xen"
                            }
                    }
                }
            ],
            SuccessUrl =
                $"{baseUrl}/payment-complete.html" +
                "?session_id={CHECKOUT_SESSION_ID}",
            CancelUrl = $"{baseUrl}/topup.html?cancelled=1"
        };

        try
        {
            var service = new SessionService();
            var session = await service.CreateAsync(
                options,
                new RequestOptions { ApiKey = secretKey },
                cancellationToken: context.RequestAborted);

            return string.IsNullOrWhiteSpace(session.Url)
                ? Results.Problem("Stripe did not return a checkout URL.")
                : Results.Ok(new { url = session.Url });
        }
        catch (StripeException exception)
        {
            logger.LogError(
                exception,
                "Unable to create Stripe Checkout for subscriber {SubscriberId}.",
                subscriberId);
            return Results.Problem(
                "Unable to start the payment. Please try again.");
        }
    }

    private static async Task<IResult> Webhook(
        HttpContext context,
        IConfiguration configuration,
        ILogger<Program> logger)
    {
        var webhookSecret = configuration["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            logger.LogError("Stripe:WebhookSecret is not configured.");
            return Results.Problem("Webhook secret is not configured.");
        }

        if (!context.Request.Headers.TryGetValue(
                "Stripe-Signature",
                out var signature))
        {
            return Results.BadRequest();
        }

        var json = await new StreamReader(context.Request.Body)
            .ReadToEndAsync(context.RequestAborted);

        Stripe.Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json,
                signature,
                webhookSecret,
                tolerance: 300,
                throwOnApiVersionMismatch: false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Stripe webhook signature validation failed.");
            return Results.BadRequest();
        }

        if (stripeEvent.Type is not "checkout.session.completed" and
            not "checkout.session.async_payment_succeeded")
        {
            return Results.Ok();
        }

        if (stripeEvent.Data.Object is not Session session ||
            !string.Equals(
                session.PaymentStatus,
                "paid",
                StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok();
        }

        if (!TryValidateSession(
                session,
                out var subscriberId,
                out var amountGbp))
        {
            logger.LogWarning(
                "Rejected invalid Stripe session {SessionId}.",
                session.Id);
            return Results.Ok();
        }

        try
        {
            var credited = await CreditPurchase(
                configuration,
                subscriberId,
                session.Id,
                amountGbp,
                context.RequestAborted);

            logger.LogInformation(
                credited
                    ? "Stripe payment credited. Subscriber={SubscriberId}, Amount={Amount}, Session={SessionId}"
                    : "Stripe payment already processed or subscriber rejected. Subscriber={SubscriberId}, Session={SessionId}",
                subscriberId,
                amountGbp,
                session.Id);
        }
        catch (SqlException exception)
        {
            logger.LogError(
                exception,
                "Failed to credit Stripe session {SessionId}.",
                session.Id);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Results.Ok();
    }

    private static bool TryValidateSession(
        Session session,
        out int subscriberId,
        out decimal amountGbp)
    {
        subscriberId = 0;
        amountGbp = 0;

        if (session.Metadata is null ||
            !session.Metadata.TryGetValue("subscriberId", out var sid) ||
            !int.TryParse(
                sid,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out subscriberId) ||
            subscriberId <= 0 ||
            !session.Metadata.TryGetValue("platformId", out var platform) ||
            platform != PlatformIds.NinjaTrader.ToString(
                CultureInfo.InvariantCulture) ||
            !string.Equals(
                session.Currency,
                "gbp",
                StringComparison.OrdinalIgnoreCase) ||
            !session.AmountSubtotal.HasValue)
        {
            return false;
        }

        amountGbp = session.AmountSubtotal.Value / 100m;
        return amountGbp == decimal.Truncate(amountGbp) &&
               AllowedTopUps.Contains((int)amountGbp);
    }

    private static async Task<bool> CreditPurchase(
        IConfiguration configuration,
        int subscriberId,
        string sessionId,
        decimal amountGbp,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(
            RequiredConnectionString(configuration));
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)
            await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            await using (var subscriberCommand = new SqlCommand("""
                SELECT COUNT(1)
                FROM dbo.Subscribers
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

                if (Convert.ToInt32(
                        await subscriberCommand.ExecuteScalarAsync(
                            cancellationToken)) != 1)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }
            }

            await using (var duplicateCommand = new SqlCommand("""
                SELECT COUNT(1)
                FROM dbo.CreditPurchases WITH (UPDLOCK, HOLDLOCK)
                WHERE OrderId = @OrderId;
                """, connection, transaction))
            {
                duplicateCommand.Parameters.Add(
                    "@OrderId",
                    SqlDbType.NVarChar,
                    255).Value = sessionId;

                if (Convert.ToInt32(
                        await duplicateCommand.ExecuteScalarAsync(
                            cancellationToken)) > 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }
            }

            await using (var insertCommand = new SqlCommand("""
                INSERT INTO dbo.CreditPurchases
                (
                    SubscriberId,
                    OrderId,
                    AmountPaid,
                    CreditRemainingGbp,
                    CreatedUtc,
                    CreditType
                )
                VALUES
                (
                    @SubscriberId,
                    @OrderId,
                    @AmountPaid,
                    @AmountPaid,
                    SYSUTCDATETIME(),
                    'Purchased'
                );
                """, connection, transaction))
            {
                insertCommand.Parameters.Add(
                    "@SubscriberId",
                    SqlDbType.Int).Value = subscriberId;
                insertCommand.Parameters.Add(
                    "@OrderId",
                    SqlDbType.NVarChar,
                    255).Value = sessionId;
                var amount = insertCommand.Parameters.Add(
                    "@AmountPaid",
                    SqlDbType.Decimal);
                amount.Precision = 10;
                amount.Scale = 2;
                amount.Value = amountGbp;
                await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<bool> IsEligibleNinjaTraderSubscriber(
        IConfiguration configuration,
        int subscriberId)
    {
        await using var connection = new SqlConnection(
            RequiredConnectionString(configuration));
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.Subscribers
            WHERE SubscriberId = @SubscriberId
              AND PlatformId = @PlatformId
              AND EmailVerified = 1
              AND Status = 1;
            """, connection);
        command.Parameters.Add(
            "@SubscriberId",
            SqlDbType.Int).Value = subscriberId;
        command.Parameters.Add(
            "@PlatformId",
            SqlDbType.Int).Value = PlatformIds.NinjaTrader;
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static string RequiredConnectionString(
        IConfiguration configuration) =>
        configuration.GetConnectionString("CodePilot") ??
        throw new InvalidOperationException(
            "ConnectionStrings:CodePilot is not configured.");
}
