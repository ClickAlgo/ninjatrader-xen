using System.Runtime.CompilerServices;

namespace NinjaTrader_Xen.Tests;

public sealed class RequestRouterPromptTests
{
    [Fact]
    public void ExistingProjectFocusedChangesDoNotRequirePlanningByDefault()
    {
        var root = GetProjectRoot();
        var prompt = File.ReadAllText(Path.Combine(
            root, "SystemPrompts", "v1", "request-router.txt"));

        Assert.Contains("When existing project code is present", prompt);
        Assert.Contains("Add a configurable", prompt);
        Assert.Contains("20-tick trailing stop", prompt);
    }

    private static string GetProjectRoot(
        [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, ".."));
}
