using System.Runtime.CompilerServices;

namespace NinjaTrader_Xen.Tests;

public sealed class BuildCheckHistoryTests
{
    [Fact]
    public void Workspace_RestoresOnlyTheLatestBuildCheck()
    {
        var script = ReadWorkspaceScript();

        Assert.Contains("function compactPreflightBuildHistory(turns)", script);
        Assert.Contains("turns.findLastIndex(isPreflightBuildReport)", script);
        Assert.Contains(
            "history = compactPreflightBuildHistory(\n        Array.isArray(project.messages) ? project.messages : []);",
            script);
    }

    [Fact]
    public void Workspace_ReplacesThePreviousBuildCheckWithoutRemovingRepairs()
    {
        var script = ReadWorkspaceScript();
        var start = script.IndexOf(
            "function renderPreflightBuildResult",
            StringComparison.Ordinal);
        var end = script.IndexOf(
            "function startPreflightRepair",
            start,
            StringComparison.Ordinal);
        var renderFunction = script[start..end];

        Assert.Contains(
            "history = history.filter(turn => !isPreflightBuildReport(turn));",
            renderFunction);
        Assert.Contains(
            "messages.querySelectorAll(\".preflight-build-message\")",
            renderFunction);
        Assert.DoesNotContain("history = [];", renderFunction);
    }

    [Fact]
    public void Workspace_CompactsBuildChecksBeforeSaving()
    {
        var script = ReadWorkspaceScript();
        var start = script.IndexOf(
            "async function saveCurrentProject",
            StringComparison.Ordinal);
        var end = script.IndexOf(
            "async function openCodeWorkspace",
            start,
            StringComparison.Ordinal);
        var saveFunction = script[start..end];

        Assert.Contains(
            "history = compactPreflightBuildHistory(history);",
            saveFunction);
    }

    private static string ReadWorkspaceScript()
    {
        var root = GetProjectRoot();
        return File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "js",
            "workspace.js"));
    }

    private static string GetProjectRoot(
        [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            ".."));
}
