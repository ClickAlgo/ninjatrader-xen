using System.Runtime.CompilerServices;

namespace NinjaTrader_Xen.Tests;

public sealed class PromptBuilderRetrievalContextTests
{
    [Fact]
    public void Workspace_PreservesOriginalPromptForBuildPlanRetrieval()
    {
        var root = GetProjectRoot();
        var script = File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "js",
            "workspace.js"));

        Assert.Contains("originalPrompt: originalPrompt?.trim()", script);
        Assert.Contains("retrievalPrompt: buildRetrievalPrompt(prompt)", script);
        Assert.Contains("Current Build Plan step:", script);
    }

    private static string GetProjectRoot(
        [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            ".."));
}
