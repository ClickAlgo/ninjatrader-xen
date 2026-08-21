using System.Runtime.CompilerServices;
using System.Text.Json;
using NinjaTrader_Xen.Endpoints;
using NinjaTrader_Xen.Models;

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
        Assert.Contains("pinnedMarker.textContent = \"Pinned\"", script);
        Assert.Contains("id=\"revisionControls\"", html);
        Assert.Contains("id=\"deleteUnpinnedRevisionsButton\"", html);

        var css = ReadProjectFile("wwwroot", "css", "site.css");
        Assert.Contains("grid-template-columns: minmax(0, 1fr);", css);
        Assert.Contains(".revision-actions {", css);
        Assert.Contains("padding: 0 8px;", css);
        Assert.Contains(".revision-pinned-marker", css);
        Assert.Contains(".revision-item.current.pinned", css);
        Assert.Contains(".revision-item.selected.pinned:not(.current)", css);
        Assert.DoesNotContain("inset 4px 0 0 var(--primary)", css);
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
    public void SnapshotDetails_ReturnThePromptThatGeneratedTheSelectedSource()
    {
        const string firstCode = "public class First : Strategy { }";
        const string revisedCode = "public class Revised : Strategy { }";
        var messagesJson = JsonSerializer.Serialize(new ChatTurn[]
        {
            new("user", "Build the first strategy"),
            new("assistant", $"```csharp\n{firstCode}\n```"),
            new("user", "Add a trailing stop"),
            new("assistant", $"Here is the revision.\n```csharp\n{revisedCode}\n```")
        });

        Assert.Equal(
            "Add a trailing stop",
            ProjectEndpoints.ExtractRevisionPrompt(messagesJson, revisedCode));
    }

    [Fact]
    public void SnapshotDetails_UseLatestUserPromptForLegacyUnmatchedSource()
    {
        var messagesJson = JsonSerializer.Serialize(new ChatTurn[]
        {
            new("user", "Use this uploaded strategy as the current source"),
            new("assistant", "Source received.")
        });

        Assert.Equal(
            "Use this uploaded strategy as the current source",
            ProjectEndpoints.ExtractRevisionPrompt(
                messagesJson,
                "public class Uploaded : Strategy { }"));
        Assert.Null(ProjectEndpoints.ExtractRevisionPrompt("not json", "source"));
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

    [Fact]
    public void SnapshotUi_ShowsGeneratingPromptAboveTheCodePreview()
    {
        var html = ReadProjectFile("wwwroot", "workspace.html");
        var script = ReadWorkspaceScript();
        var css = ReadProjectFile("wwwroot", "css", "site.css");

        Assert.Contains("id=\"codeWorkspacePrompt\"", html);
        Assert.Contains("Prompt used for this snapshot", html);
        Assert.Contains("setCodeWorkspacePrompt(result.prompt)", script);
        Assert.Contains("previewCurrent: true", script);
        Assert.Contains(".code-workspace-prompt", css);
        Assert.Contains("white-space: pre-wrap", css);
    }

    [Fact]
    public void SnapshotUi_DisablesHistoryUntilASnapshotExists()
    {
        var html = ReadProjectFile("wwwroot", "workspace.html");
        var script = ReadWorkspaceScript();
        var css = ReadProjectFile("wwwroot", "css", "site.css");

        Assert.Contains("id=\"codeViewButton\"", html);
        Assert.Contains("disabled>History</button>", html);
        Assert.Contains("let hasProjectSnapshots = false;", script);
        Assert.Contains("codeViewButton.disabled = !hasProjectSnapshots", script);
        Assert.Contains("hasProjectSnapshots = Boolean(project.latestCode)", script);
        Assert.Contains("if (latestCode) {", script);
        Assert.Contains("hasProjectSnapshots = false;", script);
        Assert.Contains(".workspace-action:disabled", css);
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
