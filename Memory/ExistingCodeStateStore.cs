using Microsoft.Data.SqlClient;
using NinjaTrader_Xen.Models;
using System.Data;
using System.Text.Json;

namespace NinjaTrader_Xen.Memory;

public interface IExistingCodeStateStore
{
    Task<ExistingCodeState?> GetAsync(int subscriberId, Guid projectId,
        CancellationToken cancellationToken);
    Task SaveAsync(int subscriberId, Guid projectId, string task,
        ExistingCodeState state, CancellationToken cancellationToken);
}

public sealed class SqlExistingCodeStateStore(IConfiguration configuration)
    : IExistingCodeStateStore
{
    public async Task<ExistingCodeState?> GetAsync(int subscriberId, Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var connection = await Open(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT StateJson FROM dbo.ExistingCodeProjectStates
            WHERE SubscriberId = @SubscriberId AND ConversationId = @ProjectId;
            """, connection);
        AddIdentity(command, subscriberId, projectId);
        var json = await command.ExecuteScalarAsync(cancellationToken) as string;
        return string.IsNullOrWhiteSpace(json) ? null
            : JsonSerializer.Deserialize<ExistingCodeState>(json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public async Task SaveAsync(int subscriberId, Guid projectId, string task,
        ExistingCodeState state, CancellationToken cancellationToken)
    {
        if (!ExistingCodeContext.IsExistingCodeTask(task))
            throw new InvalidOperationException("Existing-code state is available only for existing-code tasks.");
        var json = JsonSerializer.Serialize(state,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using var connection = await Open(cancellationToken);
        await using var command = new SqlCommand("""
            UPDATE dbo.ExistingCodeProjectStates
            SET Task = @Task, StateJson = @StateJson, UpdatedUtc = SYSUTCDATETIME()
            WHERE SubscriberId = @SubscriberId AND ConversationId = @ProjectId;
            IF @@ROWCOUNT = 0
                INSERT dbo.ExistingCodeProjectStates
                    (ConversationId, SubscriberId, Task, StateJson, UpdatedUtc)
                VALUES (@ProjectId, @SubscriberId, @Task, @StateJson, SYSUTCDATETIME());
            """, connection);
        AddIdentity(command, subscriberId, projectId);
        command.Parameters.Add("@Task", SqlDbType.NVarChar, 80).Value = task;
        command.Parameters.Add("@StateJson", SqlDbType.NVarChar, -1).Value = json;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqlConnection> Open(CancellationToken cancellationToken)
    {
        var value = configuration.GetConnectionString("CodePilot")
            ?? throw new InvalidOperationException("ConnectionStrings:CodePilot is not configured.");
        var connection = new SqlConnection(value);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void AddIdentity(SqlCommand command, int subscriberId, Guid projectId)
    {
        command.Parameters.Add("@SubscriberId", SqlDbType.Int).Value = subscriberId;
        command.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = projectId;
    }
}

public static class ExistingCodeContext
{
    public static bool IsExistingCodeTask(string? task) => task is
        "existing-strategy" or "existing-indicator";

    public static string Build(ExistingCodeState state)
    {
        var sources = string.Join("\n\n", state.Sources.Select((source, index) =>
            $"SOURCE {index + 1}: {source.FileName} ({source.Role})\nSOURCE START\n" +
            source.Code + "\nSOURCE END"));
        var decisions = string.Join("\n\n", state.Decisions.Select((decision, index) =>
            $"DECISION {index + 1}\nUser: {decision.UserText}\nXen: {decision.AssistantText}"));
        return "AUTHORITATIVE EXISTING-CODE TASK STATE\n" +
            "Use all supplied sources and carry every confirmed user decision forward. " +
            "Do not ask for source already listed here.\n\n" + sources +
            (decisions.Length == 0 ? "" :
                "\n\nREQUIREMENTS, CLARIFICATIONS, AND USER DECISIONS\n" + decisions) +
            (string.IsNullOrWhiteSpace(state.WorkingCode) ? "" :
                "\n\nLATEST WORKING VERSION START\n" + state.WorkingCode +
                "\nLATEST WORKING VERSION END");
    }
}
