using Microsoft.Data.SqlClient;
using NinjaTrader_Xen.Models;
using System.Data;
using System.Text.RegularExpressions;

namespace NinjaTrader_Xen.Memory;

public sealed record ProjectMemoryTurn(
    string Task,
    string UserSummary,
    string AssistantSummary,
    bool GeneratedCode);

public sealed record ProjectMemoryUpdate(
    int SubscriberId,
    Guid ProjectId,
    string Task,
    string UserPrompt,
    string AssistantText,
    string LatestCode);

public interface IProjectMemoryStore
{
    Task SeedIfEmptyAsync(
        int subscriberId,
        Guid projectId,
        string task,
        IReadOnlyList<ChatTurn>? history,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProjectMemoryTurn>> GetLatestTurnsAsync(
        int subscriberId,
        Guid projectId,
        int count,
        CancellationToken cancellationToken);

    Task AppendTurnAsync(
        ProjectMemoryUpdate update,
        CancellationToken cancellationToken);
}

public sealed class SqlProjectMemoryStore(
    IConfiguration configuration) : IProjectMemoryStore
{
    private const int RetainedTurns = 5;
    private const string BuildRepairPrefix =
        "Repair the latest complete NinjaScript source so it passes";
    private const string RequirementsRepairPrefix =
        "Repair the latest complete NinjaScript source to address only the";

    public async Task SeedIfEmptyAsync(
        int subscriberId,
        Guid projectId,
        string task,
        IReadOnlyList<ChatTurn>? history,
        CancellationToken cancellationToken)
    {
        if (subscriberId <= 0 || projectId == Guid.Empty || history is null)
            return;

        var turns = CompressHistory(task, history, RetainedTurns);
        if (turns.Count == 0)
            return;

        await using var connection = await OpenConnection(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            await using var existsCommand = new SqlCommand("""
                SELECT COUNT(1)
                FROM dbo.ProjectMemoryTurns
                WHERE SubscriberId = @SubscriberId
                  AND ConversationId = @ProjectId;
                """, connection, transaction);
            AddIdentityParameters(
                existsCommand,
                subscriberId,
                projectId);

            if (Convert.ToInt32(await existsCommand.ExecuteScalarAsync(
                    cancellationToken)) > 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            foreach (var turn in turns)
            {
                await InsertTurn(
                    connection,
                    transaction,
                    subscriberId,
                    projectId,
                    turn,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<ProjectMemoryTurn>> GetLatestTurnsAsync(
        int subscriberId,
        Guid projectId,
        int count,
        CancellationToken cancellationToken)
    {
        if (subscriberId <= 0 || projectId == Guid.Empty)
            return [];

        count = Math.Clamp(count, 1, 10);
        await using var connection = await OpenConnection(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT TOP (@Count)
                Task,
                UserSummary,
                AssistantSummary,
                GeneratedCode
            FROM dbo.ProjectMemoryTurns
            WHERE SubscriberId = @SubscriberId
              AND ConversationId = @ProjectId
            ORDER BY CreatedUtc DESC, Id DESC;
            """, connection);
        command.Parameters.Add("@Count", SqlDbType.Int).Value = count;
        AddIdentityParameters(command, subscriberId, projectId);

        var turns = new List<ProjectMemoryTurn>();
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            turns.Add(new ProjectMemoryTurn(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3)));
        }

        turns.Reverse();
        return turns;
    }

    public async Task AppendTurnAsync(
        ProjectMemoryUpdate update,
        CancellationToken cancellationToken)
    {
        if (update.SubscriberId <= 0 || update.ProjectId == Guid.Empty ||
            IsAutomatedRepair(update.UserPrompt))
        {
            return;
        }

        var turn = new ProjectMemoryTurn(
            update.Task,
            CleanUserMemory(update.UserPrompt),
            CleanAssistantMemory(update.AssistantText),
            !string.IsNullOrWhiteSpace(update.LatestCode));

        await using var connection = await OpenConnection(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            await InsertTurn(
                connection,
                transaction,
                update.SubscriberId,
                update.ProjectId,
                turn,
                cancellationToken);

            await using var trimCommand = new SqlCommand("""
                ;WITH OldTurns AS
                (
                    SELECT
                        Id,
                        ROW_NUMBER() OVER (
                            ORDER BY CreatedUtc DESC, Id DESC) AS RowNumber
                    FROM dbo.ProjectMemoryTurns
                    WHERE SubscriberId = @SubscriberId
                      AND ConversationId = @ProjectId
                )
                DELETE FROM OldTurns
                WHERE RowNumber > @RetainedTurns;
                """, connection, transaction);
            AddIdentityParameters(
                trimCommand,
                update.SubscriberId,
                update.ProjectId);
            trimCommand.Parameters.Add(
                "@RetainedTurns",
                SqlDbType.Int).Value = RetainedTurns;
            await trimCommand.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public static string ExtractLatestCode(
        IReadOnlyList<ChatTurn>? history)
    {
        if (history is null)
            return string.Empty;

        for (var index = history.Count - 1; index >= 0; index--)
        {
            var matches = Regex.Matches(
                history[index].Content ?? string.Empty,
                @"```(?:csharp|cs)?\s*([\s\S]*?)```",
                RegexOptions.IgnoreCase);
            if (matches.Count > 0)
                return matches[matches.Count - 1].Groups[1].Value.Trim();
        }

        return string.Empty;
    }

    internal static IReadOnlyList<ProjectMemoryTurn> CompressHistory(
        string task,
        IReadOnlyList<ChatTurn> history,
        int count)
    {
        var turns = new List<ProjectMemoryTurn>();
        string? userPrompt = null;

        foreach (var item in history)
        {
            if (item.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
            {
                userPrompt = item.Content;
                continue;
            }

            if (!item.Role.Equals(
                    "assistant",
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(userPrompt) ||
                IsAutomatedRepair(userPrompt))
            {
                continue;
            }

            turns.Add(new ProjectMemoryTurn(
                task,
                CleanUserMemory(userPrompt),
                CleanAssistantMemory(item.Content),
                !string.IsNullOrWhiteSpace(ExtractLatestCode([item]))));
            userPrompt = null;
        }

        return turns.TakeLast(count).ToList();
    }

    internal static bool IsAutomatedRepair(string? prompt) =>
        !string.IsNullOrWhiteSpace(prompt) &&
        (prompt.TrimStart().StartsWith(
             BuildRepairPrefix,
             StringComparison.OrdinalIgnoreCase) ||
         prompt.TrimStart().StartsWith(
             RequirementsRepairPrefix,
             StringComparison.OrdinalIgnoreCase));

    internal static string CleanUserMemory(string value) =>
        CleanForMemory(value, 350, replaceCode: true);

    internal static string CleanAssistantMemory(string value)
    {
        var text = Regex.Replace(
            value ?? string.Empty,
            @"```[\s\S]*?```",
            string.Empty,
            RegexOptions.Multiline);
        text = Regex.Replace(text, @"(?is)#{0,3}\s*Next step.*$", "");
        text = Regex.Replace(text, @"(?im)^#{1,6}\s*What changed\s*$", "");
        text = Regex.Replace(text, @"(?im)^#{1,6}\s*.*$", "");
        text = Regex.Replace(text, @"(?m)^\s*[-•]\s*", "");
        return CleanForMemory(text, 700, replaceCode: false);
    }

    private static string CleanForMemory(
        string value,
        int maxCharacters,
        bool replaceCode)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (replaceCode)
        {
            value = Regex.Replace(
                value,
                @"```[\s\S]*?```",
                "[code omitted]");
        }

        value = Regex.Replace(value, @"\s+", " ").Trim();
        return value.Length <= maxCharacters
            ? value
            : value[..maxCharacters].Trim() + "...";
    }

    private async Task<SqlConnection> OpenConnection(
        CancellationToken cancellationToken)
    {
        var connectionString =
            configuration.GetConnectionString("CodePilot") ??
            throw new InvalidOperationException(
                "ConnectionStrings:CodePilot is not configured.");
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task InsertTurn(
        SqlConnection connection,
        SqlTransaction transaction,
        int subscriberId,
        Guid projectId,
        ProjectMemoryTurn turn,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT INTO dbo.ProjectMemoryTurns
            (
                SubscriberId,
                SessionId,
                ConversationId,
                Task,
                UserSummary,
                AssistantSummary,
                GeneratedCode,
                CreatedUtc
            )
            VALUES
            (
                @SubscriberId,
                @SessionId,
                @ProjectId,
                @Task,
                @UserSummary,
                @AssistantSummary,
                @GeneratedCode,
                SYSUTCDATETIME()
            );
            """, connection, transaction);
        AddIdentityParameters(command, subscriberId, projectId);
        command.Parameters.Add("@SessionId", SqlDbType.NVarChar, 100).Value =
            projectId.ToString("N");
        command.Parameters.Add("@Task", SqlDbType.NVarChar, 80).Value =
            turn.Task ?? string.Empty;
        command.Parameters.Add("@UserSummary", SqlDbType.NVarChar, -1).Value =
            turn.UserSummary;
        command.Parameters.Add("@AssistantSummary", SqlDbType.NVarChar, -1).Value =
            turn.AssistantSummary;
        command.Parameters.Add("@GeneratedCode", SqlDbType.Bit).Value =
            turn.GeneratedCode;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddIdentityParameters(
        SqlCommand command,
        int subscriberId,
        Guid projectId)
    {
        command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value =
            subscriberId;
        command.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value =
            projectId;
    }
}
