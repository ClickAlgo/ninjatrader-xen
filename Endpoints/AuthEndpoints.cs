using Microsoft.Data.SqlClient;
using NinjaTrader_Xen.Models;
using NinjaTrader_Xen.Options;
using NinjaTrader_Xen.Security;
using NinjaTrader_Xen.Services;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NinjaTrader_Xen.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/register", Register);
        app.MapGet("/api/auth/verify-email", VerifyEmail);
        app.MapPost("/api/auth/login", Login);
        app.MapGet("/api/auth/me", Me).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        HttpContext context,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        AccountEmailSender emailSender,
        DisposableEmailGuard disposableEmailGuard)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) ||
            !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return Results.BadRequest(new { message = "Invalid email address." });

        if (!await disposableEmailGuard.IsAllowedAsync(
                email,
                context.RequestAborted))
        {
            return Results.BadRequest(new
            {
                message = "Disposable email addresses are not allowed."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Password) ||
            request.Password.Length is < 10 or > 128)
            return Results.BadRequest(new { message = "Password must contain between 10 and 128 characters." });

        await using var connection = new SqlConnection(RequiredConnectionString(configuration));
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            int? subscriberId = null;
            var existingAccountVerified = false;
            var resumedUnverifiedRegistration = false;

            await using (var existsCommand = new SqlCommand("""
                SELECT TOP (1) SubscriberId, EmailVerified
                FROM dbo.Subscribers
                WHERE Email = @Email
                  AND PlatformId = @PlatformId;
                """, connection, transaction))
            {
                existsCommand.Parameters.Add("@Email", SqlDbType.NVarChar, 254).Value = email;
                existsCommand.Parameters.Add("@PlatformId", SqlDbType.Int).Value = PlatformIds.NinjaTrader;

                await using var reader = await existsCommand.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    subscriberId = reader.GetInt32(reader.GetOrdinal("SubscriberId"));
                    existingAccountVerified = reader.GetBoolean(reader.GetOrdinal("EmailVerified"));
                }
            }

            if (existingAccountVerified)
                return Results.BadRequest(new { message = "Email already registered for NinjaTrader Xen." });

            var passwordHash = PasswordHasher.Hash(request.Password);

            if (subscriberId is null)
            {
                await using var insertCommand = new SqlCommand("""
                    INSERT INTO dbo.Subscribers
                        (Email, PasswordHash, EmailVerified, Status, CreatedUtc, RegistrationIp, PlatformId)
                    OUTPUT INSERTED.SubscriberId
                    VALUES
                        (@Email, @PasswordHash, 0, 0, SYSUTCDATETIME(), @RegistrationIp, @PlatformId);
                    """, connection, transaction);
                insertCommand.Parameters.Add("@Email", SqlDbType.NVarChar, 254).Value = email;
                insertCommand.Parameters.Add("@PasswordHash", SqlDbType.NVarChar, 512).Value = passwordHash;
                insertCommand.Parameters.Add("@RegistrationIp", SqlDbType.VarChar, 45)
                    .Value = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                insertCommand.Parameters.Add("@PlatformId", SqlDbType.Int).Value = PlatformIds.NinjaTrader;
                subscriberId = Convert.ToInt32(await insertCommand.ExecuteScalarAsync());
            }
            else
            {
                resumedUnverifiedRegistration = true;
                await using var refreshAccountCommand = new SqlCommand("""
                    UPDATE dbo.Subscribers
                    SET PasswordHash = @PasswordHash,
                        RegistrationIp = @RegistrationIp,
                        UpdatedUtc = SYSUTCDATETIME()
                    WHERE SubscriberId = @SubscriberId
                      AND PlatformId = @PlatformId
                      AND EmailVerified = 0;

                    UPDATE dbo.EmailVerificationTokens
                    SET UsedUtc = SYSUTCDATETIME()
                    WHERE SubscriberId = @SubscriberId
                      AND UsedUtc IS NULL;
                    """, connection, transaction);
                refreshAccountCommand.Parameters.Add("@PasswordHash", SqlDbType.NVarChar, 512).Value = passwordHash;
                refreshAccountCommand.Parameters.Add("@RegistrationIp", SqlDbType.VarChar, 45)
                    .Value = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                refreshAccountCommand.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId.Value;
                refreshAccountCommand.Parameters.Add("@PlatformId", SqlDbType.Int).Value = PlatformIds.NinjaTrader;
                await refreshAccountCommand.ExecuteNonQueryAsync();
            }

            var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));

            await using (var tokenCommand = new SqlCommand("""
                INSERT INTO dbo.EmailVerificationTokens
                    (SubscriberId, TokenHash, ExpiresUtc)
                VALUES
                    (@SubscriberId, @TokenHash, DATEADD(hour, 24, SYSUTCDATETIME()));
                """, connection, transaction))
            {
                tokenCommand.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId.Value;
                tokenCommand.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
                await tokenCommand.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();

            var baseUrl = environment.IsDevelopment()
                ? $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}".TrimEnd('/')
                : configuration["App:BaseUrl"]?.TrimEnd('/');

            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("App:BaseUrl is not configured.");

            var verificationUrl =
                $"{baseUrl}/api/auth/verify-email?token={Uri.EscapeDataString(rawToken)}";

            await emailSender.SendVerificationAsync(email, verificationUrl);
            return Results.Ok(new
            {
                subscriberId = subscriberId.Value,
                emailVerificationSent = true,
                registrationResumed = resumedUnverifiedRegistration
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<IResult> VerifyEmail(string token, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Results.BadRequest(new { message = "Missing verification token." });

        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        await using var connection = new SqlConnection(RequiredConnectionString(configuration));
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            int? subscriberId;
            await using (var selectCommand = new SqlCommand("""
                SELECT TOP (1) s.SubscriberId
                FROM dbo.EmailVerificationTokens AS t
                INNER JOIN dbo.Subscribers AS s ON s.SubscriberId = t.SubscriberId
                WHERE t.TokenHash = @TokenHash
                  AND t.UsedUtc IS NULL
                  AND t.ExpiresUtc > SYSUTCDATETIME()
                  AND s.PlatformId = @PlatformId;
                """, connection, transaction))
            {
                selectCommand.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
                selectCommand.Parameters.Add("@PlatformId", SqlDbType.Int).Value = PlatformIds.NinjaTrader;
                subscriberId = await selectCommand.ExecuteScalarAsync() as int?;
            }

            if (subscriberId is null)
                return Results.BadRequest(new { message = "Invalid or expired verification token." });

            await using (var updateSubscriber = new SqlCommand("""
                UPDATE dbo.Subscribers
                SET EmailVerified = 1,
                    EmailVerifiedUtc = SYSUTCDATETIME(),
                    Status = 1,
                    UpdatedUtc = SYSUTCDATETIME()
                WHERE SubscriberId = @SubscriberId
                  AND PlatformId = @PlatformId;
                """, connection, transaction))
            {
                updateSubscriber.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId.Value;
                updateSubscriber.Parameters.Add("@PlatformId", SqlDbType.Int).Value = PlatformIds.NinjaTrader;
                await updateSubscriber.ExecuteNonQueryAsync();
            }

            await using (var updateToken = new SqlCommand("""
                UPDATE dbo.EmailVerificationTokens
                SET UsedUtc = SYSUTCDATETIME()
                WHERE TokenHash = @TokenHash;
                """, connection, transaction))
            {
                updateToken.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
                await updateToken.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return Results.Redirect("/login.html?verified=1");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        HttpContext context,
        IConfiguration configuration,
        FreeTrialService freeTrials)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest(new { message = "Invalid login." });

        await using var connection = new SqlConnection(RequiredConnectionString(configuration));
        await connection.OpenAsync();

        await using var command = new SqlCommand("""
            SELECT SubscriberId, Email, PasswordHash, Status
            FROM dbo.Subscribers
            WHERE Email = @Email
              AND PlatformId = @PlatformId
              AND EmailVerified = 1;
            """, connection);
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 254).Value = email;
        command.Parameters.Add("@PlatformId", SqlDbType.Int).Value = PlatformIds.NinjaTrader;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Results.BadRequest(new { message = "Invalid email or password." });

        var subscriberId = reader.GetInt32(reader.GetOrdinal("SubscriberId"));
        var storedEmail = reader.GetString(reader.GetOrdinal("Email"));
        var passwordHash = reader.GetString(reader.GetOrdinal("PasswordHash"));
        var status = reader.GetByte(reader.GetOrdinal("Status"));

        if (status != 1 || !PasswordHasher.Verify(request.Password, passwordHash))
            return Results.BadRequest(new { message = "Invalid email or password." });

        await reader.CloseAsync();

        var trial = await freeTrials.GrantIfEligibleAsync(
            subscriberId,
            storedEmail,
            request.DeviceFingerprint,
            context.Connection.RemoteIpAddress?.ToString(),
            context.RequestAborted);

        return Results.Ok(new
        {
            token = JwtTokenService.Create(subscriberId, storedEmail, configuration),
            platformId = PlatformIds.NinjaTrader,
            freeTrialGranted = trial.Granted,
            freeTrialExpiresUtc = trial.ExpiresUtc
        });
    }

    private static IResult Me(HttpContext context)
    {
        return Results.Ok(new
        {
            subscriberId = context.User.FindFirstValue("sid"),
            email = context.User.FindFirstValue(ClaimTypes.Email),
            platformId = PlatformIds.NinjaTrader
        });
    }

    private static string RequiredConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString("CodePilot")
        ?? throw new InvalidOperationException("ConnectionStrings:CodePilot is not configured.");
}
