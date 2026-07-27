using Microsoft.Data.SqlClient;
using NinjaTrader_Xen.Models;
using NinjaTrader_Xen.Options;
using System.Data;
using System.Security.Claims;
using System.Text.Json;

namespace NinjaTrader_Xen.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects").RequireAuthorization();
        group.MapGet("/", List);
        group.MapGet("/{projectId:guid}", Get);
        group.MapPost("/", Save);
        group.MapPatch("/{projectId:guid}", Rename);
        group.MapDelete("/{projectId:guid}", Delete);
        return app;
    }

    private static async Task<IResult> List(
        HttpContext context,
        IConfiguration configuration)
    {
        if (!TryGetSubscriberId(context, out var subscriberId))
            return Results.Unauthorized();

        await using var connection = await OpenAuthorizedConnection(
            configuration,
            subscriberId);
        if (connection is null)
            return Results.Forbid();

        var projects = new List<object>();
        await using var command = new SqlCommand("""
            SELECT ConversationId, Title, Task, Model, CreatedUtc
            FROM dbo.SavedConversations
            WHERE SubscriberId = @SubscriberId
            ORDER BY CreatedUtc DESC;
            """, connection);
        command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            projects.Add(new
            {
                projectId = reader.GetGuid(reader.GetOrdinal("ConversationId")),
                title = reader.GetString(reader.GetOrdinal("Title")),
                task = reader.GetString(reader.GetOrdinal("Task")),
                model = reader.GetString(reader.GetOrdinal("Model")),
                updatedUtc = reader.GetDateTime(reader.GetOrdinal("CreatedUtc"))
            });
        }

        return Results.Ok(new { projects });
    }

    private static async Task<IResult> Get(
        Guid projectId,
        HttpContext context,
        IConfiguration configuration)
    {
        if (!TryGetSubscriberId(context, out var subscriberId))
            return Results.Unauthorized();

        await using var connection = await OpenAuthorizedConnection(
            configuration,
            subscriberId);
        if (connection is null)
            return Results.Forbid();

        await using var command = new SqlCommand("""
            SELECT
                sc.Title,
                sc.Task,
                sc.Model,
                sc.MessagesJson,
                sc.CreatedUtc,
                (
                    SELECT TOP (1) pr.CodeText
                    FROM dbo.ProjectRevisions AS pr
                    WHERE pr.ConversationId = sc.ConversationId
                      AND pr.SubscriberId = sc.SubscriberId
                    ORDER BY pr.CreatedUtc DESC, pr.Id DESC
                ) AS LatestCode
            FROM dbo.SavedConversations AS sc
            WHERE sc.ConversationId = @ProjectId
              AND sc.SubscriberId = @SubscriberId;
            """, connection);
        command.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = projectId;
        command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Results.NotFound();

        var messagesJson = reader.GetString(reader.GetOrdinal("MessagesJson"));
        IReadOnlyList<ChatTurn> messages;
        try
        {
            messages = JsonSerializer.Deserialize<List<ChatTurn>>(
                messagesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (JsonException)
        {
            messages = [];
        }

        return Results.Ok(new
        {
            projectId,
            title = reader.GetString(reader.GetOrdinal("Title")),
            task = reader.GetString(reader.GetOrdinal("Task")),
            model = reader.GetString(reader.GetOrdinal("Model")),
            updatedUtc = reader.GetDateTime(reader.GetOrdinal("CreatedUtc")),
            messages,
            latestCode = reader.IsDBNull(reader.GetOrdinal("LatestCode"))
                ? null
                : reader.GetString(reader.GetOrdinal("LatestCode"))
        });
    }

    private static async Task<IResult> Save(
        SaveProjectRequest request,
        HttpContext context,
        IConfiguration configuration)
    {
        if (!TryGetSubscriberId(context, out var subscriberId))
            return Results.Unauthorized();

        if (request.ProjectId == Guid.Empty)
            return Results.BadRequest(new { message = "Project identity is required." });

        var title = NormalizeTitle(request.Title);
        if (string.IsNullOrWhiteSpace(title))
            return Results.BadRequest(new { message = "Project title is required." });

        if (request.Messages.Count is < 1 or > 100)
            return Results.BadRequest(new { message = "Project messages are invalid." });

        var messagesJson = JsonSerializer.Serialize(request.Messages);
        if (messagesJson.Length > 800_000)
            return Results.BadRequest(new { message = "Project history is too large." });

        await using var connection = await OpenAuthorizedConnection(
            configuration,
            subscriberId);
        if (connection is null)
            return Results.Forbid();

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            await using (var command = new SqlCommand("""
                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.SavedConversations
                    WHERE ConversationId = @ProjectId
                      AND SubscriberId = @SubscriberId
                )
                BEGIN
                    UPDATE dbo.SavedConversations
                    SET Title = @Title,
                        Task = @Task,
                        Model = @Model,
                        MessagesJson = @MessagesJson,
                        CreatedUtc = SYSUTCDATETIME()
                    WHERE ConversationId = @ProjectId
                      AND SubscriberId = @SubscriberId;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.SavedConversations
                    (
                        ConversationId,
                        SubscriberId,
                        Task,
                        Model,
                        Title,
                        CreatedUtc,
                        Notes,
                        MessagesJson
                    )
                    VALUES
                    (
                        @ProjectId,
                        @SubscriberId,
                        @Task,
                        @Model,
                        @Title,
                        SYSUTCDATETIME(),
                        NULL,
                        @MessagesJson
                    );
                END;
                """, connection, transaction))
            {
                command.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = request.ProjectId;
                command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
                command.Parameters.Add("@Task", SqlDbType.NVarChar, 80).Value = request.Task;
                command.Parameters.Add("@Model", SqlDbType.NVarChar, 100).Value = request.Model;
                command.Parameters.Add("@Title", SqlDbType.NVarChar, 160).Value = title;
                command.Parameters.Add("@MessagesJson", SqlDbType.NVarChar, -1).Value = messagesJson;
                await command.ExecuteNonQueryAsync();
            }

            if (!string.IsNullOrWhiteSpace(request.LatestCode))
            {
                await SaveRevisionIfChanged(
                    connection,
                    transaction,
                    subscriberId,
                    request,
                    title,
                    messagesJson);
            }

            await transaction.CommitAsync();
            return Results.Ok(new
            {
                success = true,
                projectId = request.ProjectId,
                title
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<IResult> Rename(
        Guid projectId,
        RenameProjectRequest request,
        HttpContext context,
        IConfiguration configuration)
    {
        if (!TryGetSubscriberId(context, out var subscriberId))
            return Results.Unauthorized();

        var title = NormalizeTitle(request.Title);
        if (string.IsNullOrWhiteSpace(title))
            return Results.BadRequest(new { message = "Project title is required." });

        await using var connection = await OpenAuthorizedConnection(
            configuration,
            subscriberId);
        if (connection is null)
            return Results.Forbid();

        await using var command = new SqlCommand("""
            UPDATE dbo.SavedConversations
            SET Title = @Title,
                CreatedUtc = SYSUTCDATETIME()
            WHERE ConversationId = @ProjectId
              AND SubscriberId = @SubscriberId;

            SELECT @@ROWCOUNT;
            """, connection);
        command.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = projectId;
        command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
        command.Parameters.Add("@Title", SqlDbType.NVarChar, 160).Value = title;

        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1
            ? Results.Ok(new { success = true, title })
            : Results.NotFound();
    }

    private static async Task<IResult> Delete(
        Guid projectId,
        HttpContext context,
        IConfiguration configuration)
    {
        if (!TryGetSubscriberId(context, out var subscriberId))
            return Results.Unauthorized();

        await using var connection = await OpenAuthorizedConnection(
            configuration,
            subscriberId);
        if (connection is null)
            return Results.Forbid();

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using var command = new SqlCommand("""
                DELETE FROM dbo.ProjectMemoryTurns
                WHERE ConversationId = @ProjectId
                  AND SubscriberId = @SubscriberId;

                DELETE FROM dbo.ProjectRevisions
                WHERE ConversationId = @ProjectId
                  AND SubscriberId = @SubscriberId;

                DELETE FROM dbo.SavedConversations
                WHERE ConversationId = @ProjectId
                  AND SubscriberId = @SubscriberId;

                SELECT @@ROWCOUNT;
                """, connection, transaction);
            command.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = projectId;
            command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;

            var deleted = Convert.ToInt32(await command.ExecuteScalarAsync());
            await transaction.CommitAsync();
            return deleted == 1
                ? Results.Ok(new { success = true })
                : Results.NotFound();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task SaveRevisionIfChanged(
        SqlConnection connection,
        SqlTransaction transaction,
        int subscriberId,
        SaveProjectRequest request,
        string title,
        string messagesJson)
    {
        var code = request.LatestCode!.Trim();
        if (code.Length > 500_000)
            throw new InvalidOperationException("Generated source is too large to save.");

        await using (var existingCommand = new SqlCommand("""
            SELECT TOP (1) CodeText
            FROM dbo.ProjectRevisions
            WHERE ConversationId = @ProjectId
              AND SubscriberId = @SubscriberId
            ORDER BY CreatedUtc DESC, Id DESC;
            """, connection, transaction))
        {
            existingCommand.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = request.ProjectId;
            existingCommand.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
            var existing = await existingCommand.ExecuteScalarAsync() as string;
            if (string.Equals(existing?.Trim(), code, StringComparison.Ordinal))
                return;
        }

        await using (var insertCommand = new SqlCommand("""
            INSERT INTO dbo.ProjectRevisions
            (
                ConversationId,
                SubscriberId,
                Task,
                Model,
                Title,
                Notes,
                MessagesJson,
                CodeText,
                CreatedUtc
            )
            VALUES
            (
                @ProjectId,
                @SubscriberId,
                @Task,
                @Model,
                @Title,
                NULL,
                @MessagesJson,
                @CodeText,
                SYSUTCDATETIME()
            );
            """, connection, transaction))
        {
            insertCommand.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = request.ProjectId;
            insertCommand.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
            insertCommand.Parameters.Add("@Task", SqlDbType.NVarChar, 80).Value = request.Task;
            insertCommand.Parameters.Add("@Model", SqlDbType.NVarChar, 100).Value = request.Model;
            insertCommand.Parameters.Add("@Title", SqlDbType.NVarChar, 160).Value = title;
            insertCommand.Parameters.Add("@MessagesJson", SqlDbType.NVarChar, -1).Value = messagesJson;
            insertCommand.Parameters.Add("@CodeText", SqlDbType.NVarChar, -1).Value = code;
            await insertCommand.ExecuteNonQueryAsync();
        }

        await using var trimCommand = new SqlCommand("""
            ;WITH Ranked AS
            (
                SELECT Id,
                       ROW_NUMBER() OVER (ORDER BY CreatedUtc DESC, Id DESC) AS RowNumber
                FROM dbo.ProjectRevisions
                WHERE ConversationId = @ProjectId
                  AND SubscriberId = @SubscriberId
            )
            DELETE FROM Ranked WHERE RowNumber > 50;
            """, connection, transaction);
        trimCommand.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = request.ProjectId;
        trimCommand.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
        await trimCommand.ExecuteNonQueryAsync();
    }

    private static async Task<SqlConnection?> OpenAuthorizedConnection(
        IConfiguration configuration,
        int subscriberId)
    {
        var connectionString = configuration.GetConnectionString("CodePilot")
            ?? throw new InvalidOperationException("ConnectionStrings:CodePilot is not configured.");
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

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

        if (Convert.ToInt32(await command.ExecuteScalarAsync()) == 1)
            return connection;

        await connection.DisposeAsync();
        return null;
    }

    private static bool TryGetSubscriberId(HttpContext context, out int subscriberId) =>
        int.TryParse(context.User.FindFirstValue("sid"), out subscriberId) &&
        subscriberId > 0;

    private static string NormalizeTitle(string? title)
    {
        var normalized = string.Join(
            " ",
            (title ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 120 ? normalized : normalized[..120].TrimEnd();
    }
}
