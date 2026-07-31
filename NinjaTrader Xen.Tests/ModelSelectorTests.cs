using System.Runtime.CompilerServices;

namespace NinjaTrader_Xen.Tests;

public sealed class ModelSelectorTests
{
    [Fact]
    public void ModelDropdown_ShowsLunaAndKeepsKimiHidden()
    {
        var root = GetProjectRoot();
        var html = File.ReadAllText(Path.Combine(root, "wwwroot", "workspace.html"));

        Assert.Contains("value=\"gpt-5.6-luna\"", html);
        Assert.DoesNotContain("value=\"kimi-k2.7-code\"", html);
    }

    [Fact]
    public void ModelPersistence_PreservesDefaultAndMarksLunaLowCost()
    {
        var root = GetProjectRoot();
        var script = File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "js",
            "workspace.js"));

        Assert.Contains("const defaultModel = \"gpt-5.3-codex\";", script);
        Assert.Contains("localStorage.getItem(\"nx_selected_model\")", script);
        Assert.Contains("\"gpt-5.6-luna\"", script);
    }

    private static string GetProjectRoot(
        [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            ".."));
}
