using System.Runtime.CompilerServices;
using System.Text.Json;

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
    public void ModelDropdown_ReplacesOpus5WithOpus55()
    {
        var root = GetProjectRoot();
        var html = File.ReadAllText(Path.Combine(root, "wwwroot", "workspace.html"));
        var settings = File.ReadAllText(Path.Combine(root, "chatsettings.json"));
        var script = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "workspace.js"));

        Assert.Contains("value=\"claude-opus-5-5\">Claude Opus 5.5", html);
        Assert.DoesNotContain("value=\"claude-opus-5\"", html);
        Assert.Contains("\"claude-opus-5-5\"", settings);
        Assert.DoesNotContain("\"claude-opus-5\":", settings);
        Assert.Contains("[\"claude-opus-5\", \"claude-opus-5-5\"]", script);

        using var config = JsonDocument.Parse(settings);
        var pricing = config.RootElement
            .GetProperty("Pricing")
            .GetProperty("Models")
            .GetProperty("claude-opus-5-5");
        Assert.Equal(4.00m, pricing.GetProperty("InputPer1M").GetDecimal());
        Assert.Equal(20.00m, pricing.GetProperty("OutputPer1M").GetDecimal());
    }

    [Fact]
    public void ModelDropdown_PreservesConfiguredModelOrder()
    {
        var root = GetProjectRoot();
        var html = File.ReadAllText(Path.Combine(root, "wwwroot", "workspace.html"));

        var solIndex = html.IndexOf("value=\"gpt-5.6-sol\"", StringComparison.Ordinal);
        var lunaIndex = html.IndexOf("value=\"gpt-5.6-luna\"", StringComparison.Ordinal);
        var codexIndex = html.IndexOf("value=\"gpt-5.3-codex\"", StringComparison.Ordinal);

        Assert.True(solIndex >= 0 && solIndex < codexIndex);
        Assert.True(codexIndex < lunaIndex);
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
        var unsupportedStart = script.IndexOf(
            "const imageUnsupportedModels",
            StringComparison.Ordinal);
        var unsupportedEnd = script.IndexOf(
            "]);",
            unsupportedStart,
            StringComparison.Ordinal);
        var unsupportedModels = script[unsupportedStart..unsupportedEnd];
        Assert.DoesNotContain("\"gpt-5.6-luna\"", unsupportedModels);
    }

    [Fact]
    public void OpenAiProvider_AllowsLunaImageInput()
    {
        var root = GetProjectRoot();
        var provider = File.ReadAllText(Path.Combine(
            root,
            "Services",
            "OpenAiStreamingClient.cs"));

        Assert.Contains(
            "model.Equals(\"gpt-5.6-luna\", StringComparison.OrdinalIgnoreCase)",
            provider);
    }

    private static string GetProjectRoot(
        [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            ".."));
}
