using System.Runtime.CompilerServices;
using System.Text.Json;

namespace NinjaTrader_Xen.Tests;

public sealed class ModelSelectorTests
{
    [Fact]
    public void ModelDropdown_UsesGenerationSettingsWithoutExtraVisibleOptions()
    {
        var root = GetProjectRoot();
        var html = File.ReadAllText(Path.Combine(root, "wwwroot", "workspace.html"));
        var script = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "workspace.js"));

        Assert.DoesNotContain("<optgroup", html);
        Assert.DoesNotContain("latestModelsToggle", html);
        Assert.Contains("id=\"settingsButton\"", html);
        Assert.Contains("value=\"latest\" checked", html);
        Assert.Contains("value=\"established\"", html);
        Assert.Contains("data-model-family=\"openai-sol\" hidden selected>GPT Sol 6", html);
        Assert.Contains("data-model-family=\"openai-luna\" hidden>GPT Luna 6", html);
        Assert.Contains("<legend>AI model selector</legend>", html);
        Assert.Contains("Latest models", html);
        Assert.Contains("Previous models", html);
        Assert.Contains("Recommended", html);
        Assert.Contains("Update model selector", html);
        Assert.Contains("const modelGenerationStorageKey = \"nx_model_generation\";", script);
        Assert.Contains(
            "localStorage.getItem(modelGenerationStorageKey) === \"established\"",
            script);
        Assert.Contains(
            "modelForGeneration(\n        defaultModel,",
            script.Replace("\r\n", "\n"));
        Assert.Contains("option.dataset.modelFamily === family", script);
    }

    [Fact]
    public void ModelDropdown_ShowsLunaAndKeepsKimiHidden()
    {
        var root = GetProjectRoot();
        var html = File.ReadAllText(Path.Combine(root, "wwwroot", "workspace.html"));

        Assert.Contains("value=\"gpt-5.6-luna\"", html);
        Assert.DoesNotContain("value=\"kimi-k2.7-code\"", html);
    }

    [Fact]
    public void ModelDropdown_HidesSonnetButPreservesSavedProjectCompatibility()
    {
        var root = GetProjectRoot();
        var html = File.ReadAllText(Path.Combine(root, "wwwroot", "workspace.html"));
        var script = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "workspace.js"));
        var chatEndpoint = File.ReadAllText(Path.Combine(root, "Endpoints", "ChatEndpoints.cs"));

        Assert.Contains("value=\"claude-sonnet-4-6\" data-project-only-model", html);
        Assert.Contains("hidden disabled>Claude Sonnet 4.6", html);
        Assert.Contains("makeProjectModelAvailable(projectModel);", script);
        Assert.Contains("resetProjectOnlyModels();", script);
        Assert.Contains("\"claude-sonnet-4-6\"", chatEndpoint);
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
        var html = File.ReadAllText(Path.Combine(root, "wwwroot", "workspace.html"));

        Assert.Contains("const defaultModel = \"gpt-6-sol\";", script);
        Assert.Contains("data-model-family=\"openai-sol\" hidden selected>GPT Sol 6", html);
        Assert.Contains("localStorage.getItem(\"nx_selected_model\")", script);
        Assert.Contains("modelGenerationForModel(projectModel)", script);
        Assert.Contains(
            "else\n        applyPreferredModelGeneration();",
            script.Replace("\r\n", "\n"));
        Assert.Contains("applyPreferredModelGeneration();", script);
        Assert.Contains("modelForGeneration(", script);
        Assert.Contains("option.disabled = option.hidden;", script);
        Assert.Contains("\"gpt-5.6-luna\"", script);
        Assert.Contains("\"gpt-6-luna\"", script);
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
        Assert.Contains(
            "model.Equals(\"gpt-6-sol\", StringComparison.OrdinalIgnoreCase)",
            provider);
        Assert.Contains(
            "model.Equals(\"gpt-6-luna\", StringComparison.OrdinalIgnoreCase)",
            provider);
    }

    [Fact]
    public void Gpt6Models_HavePricingAndEndpointAllowlistEntries()
    {
        var root = GetProjectRoot();
        var settings = File.ReadAllText(Path.Combine(root, "chatsettings.json"));
        var chatEndpoint = File.ReadAllText(Path.Combine(root, "Endpoints", "ChatEndpoints.cs"));
        var requirementsEndpoint = File.ReadAllText(Path.Combine(root, "Endpoints", "RequirementsEndpoints.cs"));

        using var config = JsonDocument.Parse(settings);
        var models = config.RootElement.GetProperty("Pricing").GetProperty("Models");
        Assert.Equal(2.00m, models.GetProperty("gpt-6-sol").GetProperty("InputPer1M").GetDecimal());
        Assert.Equal(10.00m, models.GetProperty("gpt-6-sol").GetProperty("OutputPer1M").GetDecimal());
        Assert.Equal(0.10m, models.GetProperty("gpt-6-luna").GetProperty("InputPer1M").GetDecimal());
        Assert.Equal(0.50m, models.GetProperty("gpt-6-luna").GetProperty("OutputPer1M").GetDecimal());

        foreach (var model in new[] { "gpt-6-sol", "gpt-6-luna" })
        {
            Assert.Contains($"\"{model}\"", chatEndpoint);
            Assert.Contains($"\"{model}\"", requirementsEndpoint);
        }
    }

    private static string GetProjectRoot(
        [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            ".."));
}
