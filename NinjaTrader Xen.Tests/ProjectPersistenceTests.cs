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

    private static string ReadWorkspaceScript()
    {
        var root = GetProjectRoot();
        return File.ReadAllText(Path.Combine(root, "wwwroot", "js", "workspace.js"));
    }

    private static string GetProjectRoot(
        [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, ".."));
}
