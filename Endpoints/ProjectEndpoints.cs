using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using NinjaTrader_Xen.Models;
using NinjaTrader_Xen.Options;
using System.Data;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NinjaTrader_Xen.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects").RequireAuthorization();
        group.MapGet("/", List);
        group.MapGet("/{projectId:guid}", Get);
        group.MapGet("/{projectId:guid}/revisions", ListRevisions);
        group.MapGet("/{projectId:guid}/revisions/{revisionId:int}", GetRevision);
        group.MapPatch("/{projectId:guid}/revisions/{revisionId:int}/pin", SetRevisionPin);
        group.MapPost("/{projectId:guid}/revisions/{revisionId:int}/restore", RestoreRevision);
        group.MapDelete("/{projectId:guid}/revisions/{revisionId:int}", DeleteRevision);
        group.MapDelete("/{projectId:guid}/revisions", DeleteUnpinnedRevisions);
        group.MapPost("/", Save);
        group.MapPatch("/{projectId:guid}", Rename);
        group.MapDelete("/all", DeleteAll);
        group.MapDelete("/{projectId:guid}", Delete);
        return app;
    }

    private static async Task<IResult> ListRevisions(
        Guid projectId,
        int page,
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

        const int pageSize = 20;
        page = Math.Max(1, page);
        var offset = (page - 1) * pageSize;
        var revisions = new List<object>();
        await using var command = new SqlCommand("""
            SELECT
                COUNT(1) AS TotalCount,
                COALESCE(SUM(CASE
                    WHEN ISNULL(Notes, N'') LIKE N'PINNED|%' THEN 1
                    ELSE 0
                END), 0) AS PinnedCount
            FROM dbo.ProjectRevisions
            WHERE ConversationId = @ProjectId
              AND SubscriberId = @SubscriberId;

            WITH Revisions AS
            (
                SELECT
                    Id,
                    Task,
                    Model,
                    Title,
                    CreatedUtc,
                    LEN(CodeText) AS CodeLength,
                    ROW_NUMBER() OVER (ORDER BY CreatedUtc, Id) AS VersionNumber,
                    COUNT(*) OVER () AS VersionCount,
                    CASE
                        WHEN ISNULL(Notes, N'') LIKE N'PINNED|%'
                            THEN CAST(1 AS bit)
                        ELSE CAST(0 AS bit)
                    END AS IsPinned
                FROM dbo.ProjectRevisions
                WHERE ConversationId = @ProjectId
                  AND SubscriberId = @SubscriberId
            )
            SELECT
                Id,
                Task,
                Model,
                Title,
                CreatedUtc,
                CodeLength,
                VersionNumber,
                VersionCount,
                IsPinned
            FROM Revisions
            ORDER BY VersionNumber DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, connection);
        command.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = projectId;
        command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
        command.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;

        await using var reader = await command.ExecuteReaderAsync();
        var totalCount = 0;
        var pinnedCount = 0;
        if (await reader.ReadAsync())
        {
            totalCount = reader.GetInt32(reader.GetOrdinal("TotalCount"));
            pinnedCount = reader.GetInt32(reader.GetOrdinal("PinnedCount"));
        }

        await reader.NextResultAsync();
        while (await reader.ReadAsync())
        {
            var versionNumber = Convert.ToInt32(reader.GetInt64(
                reader.GetOrdinal("VersionNumber")));
            var versionCount = reader.GetInt32(reader.GetOrdinal("VersionCount"));
            revisions.Add(new
            {
                revisionId = reader.GetInt32(reader.GetOrdinal("Id")),
                versionNumber,
                isCurrent = versionNumber == versionCount,
                task = reader.GetString(reader.GetOrdinal("Task")),
                model = reader.GetString(reader.GetOrdinal("Model")),
                title = reader.GetString(reader.GetOrdinal("Title")),
                createdUtc = reader.GetDateTime(reader.GetOrdinal("CreatedUtc")),
                codeLength = reader.IsDBNull(reader.GetOrdinal("CodeLength"))
                    ? 0
                    : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("CodeLength"))),
                isPinned = reader.GetBoolean(reader.GetOrdinal("IsPinned"))
            });
        }

        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        return Results.Ok(new
        {
            revisions,
            totalCount,
            pinnedCount,
            page,
            pageSize,
            totalPages
        });
    }

    private static async Task<IResult> SetRevisionPin(
        Guid projectId,
        int revisionId,
        PinRevisionRequest request,
        HttpContext context,
        IConfiguration configuration)
    {
        if (!TryGetSubscriberId(context, out var subscriberId))
            return Results.Unauthorized();

        await using var connection = await OpenAuthorizedConnection(configuration, subscriberId);
        if (connection is null)
            return Results.Forbid();

        await using var command = new SqlCommand("""
            UPDATE dbo.ProjectRevisions
            SET Notes = CASE
                WHEN @Pinned = 1
                     AND ISNULL(Notes, N'') NOT LIKE N'PINNED|%'
                    THEN CONCAT(N'PINNED|', ISNULL(Notes, N''))
                WHEN @Pinned = 0
                     AND ISNULL(Notes, N'') LIKE N'PINNED|%'
                    THEN STUFF(Notes, 1, 7, N'')
                ELSE Notes
            END
            WHERE Id = @RevisionId
              AND ConversationId = @ProjectId
              AND SubscriberId = @SubscriberId
              AND (@Pinned = 0 OR LEN(ISNULL(Notes, N'')) <= 493);

            SELECT @@ROWCOUNT;
            """, connection);
        command.Parameters.Add("@RevisionId", SqlDbType.Int).Value = revisionId;
        command.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = projectId;
        command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
        command.Parameters.Add("@Pinned", SqlDbType.Bit).Value = request.Pinned;

        var updated = Convert.ToInt32(await command.ExecuteScalarAsync());
        return updated == 1
            ? Results.Ok(new { success = true, pinned = request.Pinned })
            : Results.BadRequest(new
            {
                message = "This snapshot was not found or its existing notes are too long to preserve while pinning."
            });
    }

    private static async Task<IResult> DeleteRevision(
        Guid projectId,
        int revisionId,
        HttpContext context,
        IConfiguration configuration)
    {
        if (!TryGetSubscriberId(context, out var subscriberId))
            return Results.Unauthorized();

        await using var connection = await OpenAuthorizedConnection(configuration, subscriberId);
        if (connection is null)
            return Results.Forbid();

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using var command = new SqlCommand("""
                DELETE pr
                FROM dbo.ProjectRevisions AS pr
                WHERE pr.Id = @RevisionId
                  AND pr.ConversationId = @ProjectId
                  AND pr.SubscriberId = @SubscriberId
                  AND ISNULL(pr.Notes, N'') NOT LIKE N'PINNED|%'
                  AND pr.Id <> (
                      SELECT TOP(1) r2.Id
                      FROM dbo.ProjectRevisions AS r2
                      WHERE r2.ConversationId = @ProjectId
                        AND r2.SubscriberId = @SubscriberId
                      ORDER BY r2.CreatedUtc DESC, r2.Id DESC
                  );

                SELECT @@ROWCOUNT;
                """, connection, transaction);
            command.Parameters.Add("@RevisionId", SqlDbType.Int).Value = revisionId;
            command.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = projectId;
            command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
            var deleted = Convert.ToInt32(await command.ExecuteScalarAsync());
            await transaction.CommitAsync();
            return deleted == 1
                ? Results.Ok(new { success = true, deletedSnapshots = 1 })
                : Results.BadRequest(new
                {
                    message = "The current or a pinned snapshot cannot be deleted."
                });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<IResult> DeleteUnpinnedRevisions(
        Guid projectId,
        [FromBody] DeleteRevisionHistoryRequest request,
        HttpContext context,
        IConfiguration configuration)
    {
        if (!TryGetSubscriberId(context, out var subscriberId))
            return Results.Unauthorized();
        if (!string.Equals(request.Confirmation?.Trim(), "DELETE", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { message = "Type DELETE to delete unpinned snapshot history." });

        await using var connection = await OpenAuthorizedConnection(configuration, subscriberId);
        if (connection is null)
            return Results.Forbid();

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using var command = new SqlCommand("""
                DELETE pr
                FROM dbo.ProjectRevisions AS pr
                WHERE pr.ConversationId = @ProjectId
                  AND pr.SubscriberId = @SubscriberId
                  AND ISNULL(pr.Notes, N'') NOT LIKE N'PINNED|%'
                  AND pr.Id <> (
                      SELECT TOP(1) r2.Id
                      FROM dbo.ProjectRevisions AS r2
                      WHERE r2.ConversationId = @ProjectId
                        AND r2.SubscriberId = @SubscriberId
                      ORDER BY r2.CreatedUtc DESC, r2.Id DESC
                  );

                SELECT @@ROWCOUNT;
                """, connection, transaction);
            command.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = projectId;
            command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
            var deletedSnapshots = Convert.ToInt32(await command.ExecuteScalarAsync());
            await transaction.CommitAsync();
            return Results.Ok(new { success = true, deletedSnapshots });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<IResult> GetRevision(
        Guid projectId,
        int revisionId,
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
            SELECT Id, Task, Model, Title, CodeText, CreatedUtc
            FROM dbo.ProjectRevisions
            WHERE Id = @RevisionId
              AND ConversationId = @ProjectId
              AND SubscriberId = @SubscriberId;
            """, connection);
        command.Parameters.Add("@RevisionId", SqlDbType.Int).Value = revisionId;
        command.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = projectId;
        command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Results.NotFound();

        return Results.Ok(new
        {
            revisionId = reader.GetInt32(reader.GetOrdinal("Id")),
            task = reader.GetString(reader.GetOrdinal("Task")),
            model = reader.GetString(reader.GetOrdinal("Model")),
            title = reader.GetString(reader.GetOrdinal("Title")),
            code = reader.GetString(reader.GetOrdinal("CodeText")),
            createdUtc = reader.GetDateTime(reader.GetOrdinal("CreatedUtc"))
        });
    }

    private static async Task<IResult> RestoreRevision(
        Guid projectId,
        int revisionId,
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
            string title;
            string task;
            string model;
            string messagesJson;
            string code;

            await using (var loadCommand = new SqlCommand("""
                SELECT
                    sc.Title AS CurrentTitle,
                    pr.Task,
                    pr.Model,
                    pr.MessagesJson,
                    pr.CodeText
                FROM dbo.ProjectRevisions AS pr
                INNER JOIN dbo.SavedConversations AS sc
                    ON sc.ConversationId = pr.ConversationId
                   AND sc.SubscriberId = pr.SubscriberId
                WHERE pr.Id = @RevisionId
                  AND pr.ConversationId = @ProjectId
                  AND pr.SubscriberId = @SubscriberId;
                """, connection, transaction))
            {
                loadCommand.Parameters.Add("@RevisionId", SqlDbType.Int).Value = revisionId;
                loadCommand.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = projectId;
                loadCommand.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
                await using var reader = await loadCommand.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    await transaction.RollbackAsync();
                    return Results.NotFound();
                }

                title = reader.GetString(reader.GetOrdinal("CurrentTitle"));
                task = reader.GetString(reader.GetOrdinal("Task"));
                model = reader.GetString(reader.GetOrdinal("Model"));
                messagesJson = reader.GetString(reader.GetOrdinal("MessagesJson"));
                code = reader.GetString(reader.GetOrdinal("CodeText"));
            }

            await using (var updateCommand = new SqlCommand("""
                UPDATE dbo.SavedConversations
                SET Task = @Task,
                    Model = @Model,
                    MessagesJson = @MessagesJson,
                    CreatedUtc = SYSUTCDATETIME()
                WHERE ConversationId = @ProjectId
                  AND SubscriberId = @SubscriberId;
                """, connection, transaction))
            {
                updateCommand.Parameters.Add("@Task", SqlDbType.NVarChar, 80).Value = task;
                updateCommand.Parameters.Add("@Model", SqlDbType.NVarChar, 100).Value = model;
                updateCommand.Parameters.Add("@MessagesJson", SqlDbType.NVarChar, -1).Value = messagesJson;
                updateCommand.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = projectId;
                updateCommand.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
                await updateCommand.ExecuteNonQueryAsync();
            }

            string? latestCode;
            await using (var latestCommand = new SqlCommand("""
                SELECT TOP (1) CodeText
                FROM dbo.ProjectRevisions
                WHERE ConversationId = @ProjectId
                  AND SubscriberId = @SubscriberId
                ORDER BY CreatedUtc DESC, Id DESC;
                """, connection, transaction))
            {
                latestCommand.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = projectId;
                latestCommand.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
                latestCode = await latestCommand.ExecuteScalarAsync() as string;
            }

            if (!string.Equals(latestCode?.Trim(), code.Trim(), StringComparison.Ordinal))
            {
                await using var insertCommand = new SqlCommand("""
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
                        @Notes,
                        @MessagesJson,
                        @CodeText,
                        SYSUTCDATETIME()
                    );
                    """, connection, transaction);
                insertCommand.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = projectId;
                insertCommand.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
                insertCommand.Parameters.Add("@Task", SqlDbType.NVarChar, 80).Value = task;
                insertCommand.Parameters.Add("@Model", SqlDbType.NVarChar, 100).Value = model;
                insertCommand.Parameters.Add("@Title", SqlDbType.NVarChar, 160).Value = title;
                insertCommand.Parameters.Add("@Notes", SqlDbType.NVarChar, 500).Value =
                    $"Restored from revision {revisionId}";
                insertCommand.Parameters.Add("@MessagesJson", SqlDbType.NVarChar, -1).Value = messagesJson;
                insertCommand.Parameters.Add("@CodeText", SqlDbType.NVarChar, -1).Value = code;
                await insertCommand.ExecuteNonQueryAsync();
            }

            await using (var trimCommand = new SqlCommand("""
                ;WITH RankedOrdinary AS
                (
                    SELECT
                        Id,
                        ROW_NUMBER() OVER (
                            ORDER BY CreatedUtc DESC, Id DESC
                        ) AS RowNumber
                    FROM dbo.ProjectRevisions
                    WHERE ConversationId = @ProjectId
                      AND SubscriberId = @SubscriberId
                      AND ISNULL(Notes, N'') NOT LIKE N'PINNED|%'
                )
                DELETE FROM RankedOrdinary WHERE RowNumber > 50;
                """, connection, transaction))
            {
                trimCommand.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = projectId;
                trimCommand.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
                await trimCommand.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();

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
                success = true,
                projectId,
                title,
                task,
                model,
                messages,
                latestCode = code
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
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

        if (RequiresCompleteCodeForPersistence(request.Task) &&
            !IsCompleteNinjaScript(request.LatestCode))
        {
            return Results.BadRequest(new
            {
                message = "A complete NinjaScript Strategy or Indicator is required before this project can be saved."
            });
        }

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
                DELETE FROM dbo.ExistingCodeProjectStates
                WHERE ConversationId = @ProjectId
                  AND SubscriberId = @SubscriberId;

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

    private static async Task<IResult> DeleteAll(
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
                DECLARE @DeletedProjects int;

                DELETE FROM dbo.ExistingCodeProjectStates
                WHERE SubscriberId = @SubscriberId;

                DELETE FROM dbo.ProjectMemoryTurns
                WHERE SubscriberId = @SubscriberId;

                DELETE FROM dbo.ProjectRevisions
                WHERE SubscriberId = @SubscriberId;

                DELETE FROM dbo.SavedConversations
                WHERE SubscriberId = @SubscriberId;

                SET @DeletedProjects = @@ROWCOUNT;
                SELECT @DeletedProjects;
                """, connection, transaction);
            command.Parameters.Add("@SubscriberId", SqlDbType.Int)
                .Value = subscriberId;

            var deletedProjects = Convert.ToInt32(
                await command.ExecuteScalarAsync());
            await transaction.CommitAsync();
            return Results.Ok(new { success = true, deletedProjects });
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
            ;WITH RankedOrdinary AS
            (
                SELECT Id,
                       ROW_NUMBER() OVER (ORDER BY CreatedUtc DESC, Id DESC) AS RowNumber
                FROM dbo.ProjectRevisions
                WHERE ConversationId = @ProjectId
                  AND SubscriberId = @SubscriberId
                  AND ISNULL(Notes, N'') NOT LIKE N'PINNED|%'
            )
            DELETE FROM RankedOrdinary WHERE RowNumber > 50;
            """, connection, transaction);
        trimCommand.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = request.ProjectId;
        trimCommand.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
        await trimCommand.ExecuteNonQueryAsync();
    }

    internal static bool RequiresCompleteCodeForPersistence(string task) =>
        task is "build-strategy" or "build-indicator";

    internal static bool IsCompleteNinjaScript(string? code) =>
        !string.IsNullOrWhiteSpace(code) &&
        Regex.IsMatch(code,
            @"\bclass\s+[A-Za-z_][A-Za-z0-9_]*[\s\S]{0,300}:\s*(?:[\w.]+\.)?(?:Strategy|Indicator)\b") &&
        Regex.IsMatch(code, @"\bOnStateChange\s*\(");

    public sealed record PinRevisionRequest(bool Pinned);
    public sealed record DeleteRevisionHistoryRequest(string? Confirmation);

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
