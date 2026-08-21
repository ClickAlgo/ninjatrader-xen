using System.Runtime.CompilerServices;
using NinjaTrader_Xen.Endpoints;

namespace NinjaTrader_Xen.Tests;

public sealed class ProjectPersistenceTests
{
    private const string CompleteStrategy = """
        public class EmaStrategy : Strategy
        {
            protected override void OnStateChange() { }
        }
        """;

    [Theory]
    [InlineData("build-strategy", true)]
    [InlineData("build-indicator", true)]
    [InlineData("existing-strategy", false)]
    [InlineData("convert-indicator", false)]
    [InlineData("analyse-backtest", false)]
    public void Endpoint_CodeGateIsLimitedToNewBuildProjects(
        string task,
        bool expected)
    {
        Assert.Equal(
            expected,
            ProjectEndpoints.RequiresCompleteCodeForPersistence(task));
    }

    [Fact]
    public void Endpoint_RecognizesCompleteNinjaScript()
    {
        Assert.True(ProjectEndpoints.IsCompleteNinjaScript(CompleteStrategy));
        Assert.False(ProjectEndpoints.IsCompleteNinjaScript(null));
        Assert.False(ProjectEndpoints.IsCompleteNinjaScript(
            "Here are some suggestions without source code."));
        Assert.False(ProjectEndpoints.IsCompleteNinjaScript(
            "public class Partial : Strategy { }"));
    }

    [Fact]
    public void Workspace_SavesNewBuildOnlyForCurrentCompleteCodeResponse()
    {
        var script = ReadWorkspaceScript();
        var responseStart = script.IndexOf(
            "history.push({ role: \"assistant\", content: assistantText });",
            StringComparison.Ordinal);
        var responseEnd = script.IndexOf(
            "} catch (error)",
            responseStart,
            StringComparison.Ordinal);
        var responseFlow = script[responseStart..responseEnd];

        Assert.Contains(
            "const responseCode = extractLatestCodeBlock(assistantText);",
            responseFlow);
        Assert.Contains(
            "looksLikeCompleteNinjaScript(responseCode)",
            responseFlow);
        Assert.Contains(
            "completeResponseCode || !isNewCodeBuildTask()",
            responseFlow);
        Assert.Contains(
            "saveCurrentProject(completeResponseCode)",
            responseFlow);
    }

    [Fact]
    public void Workspace_UsesFirstCodeRequestForInitialProjectTitle()
    {
        var script = ReadWorkspaceScript();

        Assert.Contains("let currentProjectPersisted = false;", script);
        Assert.Contains("if (completeResponseCode && !currentProjectPersisted)", script);
        Assert.Contains("currentProjectTitle = createProjectTitle(titlePrompt);", script);
        Assert.Contains("currentProjectPersisted = true;", script);
    }

    [Fact]
    public void SnapshotUi_PagesPinsAndGuardsDeletion()
    {
        var script = ReadWorkspaceScript();
        var html = ReadProjectFile("wwwroot", "workspace.html");

        Assert.Contains("const revisionPageSize = 20;", script);
        Assert.Contains("revisions?page=${page}", script);
        Assert.Contains("setCodeRevisionPin", script);
        Assert.Contains("deleteCodeRevision", script);
        Assert.Contains("deleteUnpinnedCodeRevisions", script);
        Assert.Contains("confirmation?.trim().toUpperCase() !== \"DELETE\"", script);
        Assert.Contains("revision.isCurrent || revision.isPinned", script);
        Assert.Contains("page > result.totalPages", script);
        Assert.Contains("id=\"revisionControls\"", html);
        Assert.Contains("id=\"deleteUnpinnedRevisionsButton\"", html);

        var css = ReadProjectFile("wwwroot", "css", "site.css");
        Assert.Contains("grid-template-columns: minmax(0, 1fr);", css);
        Assert.Contains(".revision-actions {", css);
        Assert.Contains("padding: 0 8px;", css);
    }

    [Fact]
    public void SnapshotEndpoints_AreScopedPinnedAndCurrentProtected()
    {
        var endpoint = ReadProjectFile("Endpoints", "ProjectEndpoints.cs");

        Assert.Contains("const int pageSize = 20;", endpoint);
        Assert.Contains("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", endpoint);
        Assert.Contains("MapPatch(\"/{projectId:guid}/revisions/{revisionId:int}/pin\"", endpoint);
        Assert.Contains("MapDelete(\"/{projectId:guid}/revisions/{revisionId:int}\"", endpoint);
        Assert.Contains("MapDelete(\"/{projectId:guid}/revisions\"", endpoint);
        Assert.Contains("CONCAT(N'PINNED|', ISNULL(Notes, N''))", endpoint);
        Assert.Contains("STUFF(Notes, 1, 7, N'')", endpoint);
        Assert.Contains("SELECT TOP(1) r2.Id", endpoint);
        Assert.DoesNotContain("SELECT TOP(1 r2.Id", endpoint);
        Assert.Contains("request.Confirmation?.Trim()", endpoint);
        Assert.Contains("[FromBody] DeleteRevisionHistoryRequest request", endpoint);
        Assert.Contains("pr.SubscriberId = @SubscriberId", endpoint);
        Assert.Contains("ISNULL(pr.Notes, N'') NOT LIKE N'PINNED|%'", endpoint);
    }

    [Fact]
    public void SnapshotRetention_KeepsFiftyOrdinaryPlusPinnedAndDuplicateGuard()
    {
        var endpoint = ReadProjectFile("Endpoints", "ProjectEndpoints.cs");

        Assert.Equal(2, CountOccurrences(endpoint, "DELETE FROM RankedOrdinary WHERE RowNumber > 50"));
        Assert.Contains("RankedOrdinary AS", endpoint);
        Assert.Contains("AND ISNULL(Notes, N'') NOT LIKE N'PINNED|%'", endpoint);
        Assert.Contains("string.Equals(existing?.Trim(), code, StringComparison.Ordinal)", endpoint);
    }

    private static string ReadWorkspaceScript()
    {
        var root = GetProjectRoot();
        return File.ReadAllText(Path.Combine(root, "wwwroot", "js", "workspace.js"));
    }

    private static string ReadProjectFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([GetProjectRoot(), .. parts]));

    private static int CountOccurrences(string value, string fragment) =>
        (value.Length - value.Replace(fragment, "", StringComparison.Ordinal).Length) /
        fragment.Length;

    private static string GetProjectRoot(
        [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, ".."));
}
