namespace NinjaTrader_Xen.Tests;

using NinjaTrader_Xen.Endpoints;
using NinjaTrader_Xen.Models;

public sealed class ExistingCodeWorkflowTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void WorkspaceProvidesSeparateDurableSourceControl()
    {
        var html = File.ReadAllText(Path.Combine(Root, "wwwroot", "workspace.html"));
        var script = File.ReadAllText(Path.Combine(Root, "wwwroot", "js", "workspace.js"));

        Assert.Contains("id=\"existingCodeModal\"", html);
        Assert.Contains("Current codebase", html);
        Assert.Contains("Merge functionality into current source", html);
        Assert.Contains("Use only as an example", html);
        Assert.Contains("Replace current source", script);
        Assert.Contains("https://help.clickalgo.com/ninjatrader-xen/existing-code/", html);
        Assert.Contains("Need help adding existing code?", html);
        Assert.Contains("/existing-code", script);
        Assert.Contains("looksLikeCompleteNinjaScript(prompt)", script);
        Assert.Contains("Add the current source code before sending requirements", script);
        Assert.Contains("Review strategy", script);
        Assert.Contains("Review indicator", script);
        Assert.Contains("Review all attached NinjaScript source files", script);
        Assert.Contains("Do not modify", script);
        Assert.Contains("form.requestSubmit()", script);
        Assert.Contains("additional source", script);
        Assert.Contains("reference source", script);
        Assert.Contains("existing-code-attachment-remove", script);
        Assert.Contains("removeExistingCodeAttachment", script);
        Assert.Contains("hasCurrentExistingSource", script);
        Assert.Contains("no more than four source files", script);
    }

    [Fact]
    public void SourceAttachmentUiGateIncludesExistingAndConvertTasks()
    {
        var script = File.ReadAllText(Path.Combine(Root, "wwwroot", "js", "workspace.js"));

        Assert.Contains("function isSourceAttachmentTask", script);
        Assert.Contains("task === \"convert-strategy\" || task === \"convert-indicator\"", script);
        Assert.Contains("saveConversionSourceAttachment(fileName, code)", script);
        Assert.Contains("sources: [source]", script);
        Assert.DoesNotContain("One source file required · additional details are optional", script);
        Assert.DoesNotContain("promptInput.value = request;", script);
    }

    [Fact]
    public void ConversionTasksCanPasteOrUploadInTheSharedSourceDialog()
    {
        var html = File.ReadAllText(Path.Combine(Root, "wwwroot", "workspace.html"));
        var script = File.ReadAllText(Path.Combine(Root, "wwwroot", "js", "workspace.js"));
        var styles = File.ReadAllText(Path.Combine(Root, "wwwroot", "css", "site.css"));

        Assert.Contains("if (isSourceAttachmentTask())", script);
        Assert.Contains("button: \"Share source code\"", script);
        Assert.Contains("existingCodeText.value = source", script);
        Assert.Contains("openExistingCodeModal();", script);
        Assert.Contains("Paste the complete source code from the original platform", script);
        Assert.Contains("existingCodeRoleField.hidden = conversion", script);
        Assert.Contains("await saveConversionSourceAttachment(fileName, code)", script);
        Assert.Contains("Or upload source file", html);
        Assert.Contains(".feedback-field[hidden]", styles);
    }

    [Fact]
    public void AttachedSourceTasksCanBeSubmittedWithoutExtraInstructions()
    {
        var html = File.ReadAllText(Path.Combine(Root, "wwwroot", "workspace.html"));
        var script = File.ReadAllText(Path.Combine(Root, "wwwroot", "js", "workspace.js"));

        Assert.DoesNotContain("placeholder=\"Describe your NinjaTrader strategy…\" required", html);
        Assert.Contains("const sourceTaskWithSource =", script);
        Assert.Contains("Review the attached NinjaTrader strategy source.", script);
        Assert.Contains("Review the attached NinjaTrader indicator source.", script);
        Assert.Contains("Convert the attached strategy to NinjaTrader 8.", script);
        Assert.Contains("Convert the attached indicator to NinjaTrader 8.", script);
        Assert.Contains("Enter a request before sending", script);
    }

    [Fact]
    public void AllSourceTasksTreatComposerTextAsOptionalAdditionalInformation()
    {
        var script = File.ReadAllText(Path.Combine(Root, "wwwroot", "js", "workspace.js"));

        Assert.Contains("Optional: describe any changes, errors or strategy behaviour you want reviewed", script);
        Assert.Contains("Optional: describe any changes, errors, calculations or chart display you want reviewed", script);
        Assert.Contains("Optional: explain how you want the converted strategy to behave", script);
        Assert.Contains("Optional: explain how the converted indicator should calculate or appear on the chart", script);
        Assert.DoesNotContain("Source code required · additional details are optional", script);
        Assert.DoesNotContain("Source attached · additional details are optional", script);
        Assert.DoesNotContain("platform mappings", script);
    }

    [Fact]
    public void StrategyConversionKeepsTimingRuleButMovesWarningOutOfTaskIntro()
    {
        var html = File.ReadAllText(Path.Combine(Root, "wwwroot", "workspace.html"));
        var script = File.ReadAllText(Path.Combine(Root, "wwwroot", "js", "workspace.js"));
        var prompt = File.ReadAllText(Path.Combine(
            Root, "SystemPrompts", "v1", "convert-strategy.txt"));
        var indicatorPrompt = File.ReadAllText(Path.Combine(
            Root, "SystemPrompts", "v1", "convert-indicator.txt"));
        const string warning = "Strategy conversions may compile correctly but still behave differently because platforms process bars, ticks and orders differently. Compare entry and exit timing with the original before live use.";
        const string rule = "Preserve the source strategy’s execution timing and order behaviour as closely as NinjaTrader allows.";

        Assert.DoesNotContain(warning, script);
        Assert.Contains("A successful build confirms compatibility, not identical strategy behaviour.", html);
        Assert.Contains(rule, prompt);
        Assert.DoesNotContain(rule, indicatorPrompt);
    }

    [Fact]
    public void DatabaseConstraintMigrationAddsConvertTasks()
    {
        var migration = File.ReadAllText(Path.Combine(
            Root, "Database", "003-convert-source-project-state.sql"));

        Assert.Contains("N'convert-strategy'", migration);
        Assert.Contains("N'convert-indicator'", migration);
        Assert.DoesNotContain("build-strategy", migration);
    }

    [Fact]
    public void ExistingCodeEndpointCapsSourceBundlesAtFour()
    {
        var endpoint = File.ReadAllText(Path.Combine(
            Root, "Endpoints", "ExistingCodeEndpoints.cs"));

        Assert.Contains("sources.Count > 4", endpoint);
        Assert.Contains("no more than four source files", endpoint);
        Assert.Contains("Conversion tasks allow one source file", endpoint);
    }

    [Fact]
    public void FirstExistingSourceMustBeCurrentCodebase()
    {
        Assert.Null(ExistingCodeEndpoints.ValidateSources("existing-strategy",
            [Source("current-source")]));
        Assert.Contains("Current codebase", ExistingCodeEndpoints.ValidateSources(
            "existing-indicator", [Source("additional-source")])!);
        Assert.Contains("Current codebase", ExistingCodeEndpoints.ValidateSources(
            "existing-strategy", [Source("reference-source")])!);
    }

    [Fact]
    public void ExistingSourcesAllowSupportOnlyAfterCurrentExists()
    {
        Assert.Null(ExistingCodeEndpoints.ValidateSources("existing-strategy",
            [Source("current-source"), Source("additional-source"), Source("reference-source")]));
    }

    [Fact]
    public void ExistingSourcesRejectMultipleCurrentAndInvalidRoles()
    {
        Assert.Contains("Only one", ExistingCodeEndpoints.ValidateSources("existing-strategy",
            [Source("current-source"), Source("current-source")])!);
        Assert.Contains("not supported", ExistingCodeEndpoints.ValidateSources("existing-strategy",
            [Source("mystery-source")])!);
    }

    [Fact]
    public void EmptyExistingSourcesAreAllowedForClearTask()
    {
        Assert.Null(ExistingCodeEndpoints.ValidateSources("existing-indicator", []));
    }

    [Theory]
    [InlineData("convert-strategy")]
    [InlineData("convert-indicator")]
    public void ConversionTasksKeepOneAuthoritativeSource(string task)
    {
        Assert.Null(ExistingCodeEndpoints.ValidateSources(task, [Source("current-source")]));
        Assert.Contains("one source", ExistingCodeEndpoints.ValidateSources(task,
            [Source("current-source"), Source("additional-source")])!);
    }

    [Fact]
    public void RoleChoicesStayLockedUntilCurrentSourceIsSaved()
    {
        var script = File.ReadAllText(Path.Combine(Root, "wwwroot", "js", "workspace.js"));

        Assert.Contains("existingCodeRole.disabled = !hasCurrent", script);
        Assert.Contains("role: hasCurrent ? existingCodeRole.value : \"current-source\"", script);
        Assert.Contains("Remove Merge and Example sources before removing the Current codebase", script);
    }

    private static ExistingCodeSource Source(string role) =>
        new(Guid.NewGuid().ToString("N"), "Source.cs", role, "public class Source { }");

    [Fact]
    public void ResponseActionsRequireACompleteNinjaScriptFile()
    {
        var script = File.ReadAllText(Path.Combine(Root, "wwwroot", "js", "workspace.js"));

        Assert.Contains("if (!incomplete && looksLikeCompleteNinjaScript(code))", script);
        Assert.Contains("generatedCode.push(code)", script);
        Assert.DoesNotContain(
            "appendCodeBlock(container, code);\n        generatedCode.push(code);",
            script.Replace("\r\n", "\n"));
    }
}
