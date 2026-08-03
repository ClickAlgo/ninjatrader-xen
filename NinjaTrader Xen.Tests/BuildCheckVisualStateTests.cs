using System.Runtime.CompilerServices;

namespace NinjaTrader_Xen.Tests;

public sealed class BuildCheckVisualStateTests
{
    [Fact]
    public void BuildCheckWorkingStateIsClearlyVisibleAndAccessible()
    {
        var root = GetProjectRoot();
        var styles = File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "css",
            "site.css"));
        var script = File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "js",
            "workspace.js"));

        Assert.Contains(".preflight-build-button.is-checking::before", styles);
        Assert.Contains("@keyframes build-check-spinner", styles);
        Assert.Contains("@keyframes build-check-glow", styles);
        Assert.Contains("background: linear-gradient(135deg, #1c6a45, #164d35);", styles);
        Assert.Contains("button.textContent = \"Checking build...\"", script);
        Assert.Contains("button.setAttribute(\"aria-busy\", \"true\")", script);
        Assert.Contains("button.disabled = true", script);
    }

    private static string GetProjectRoot(
        [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            ".."));
}
