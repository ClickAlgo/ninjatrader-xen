namespace NinjaTrader_Xen.Tests;

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
        Assert.Contains("Integrate with current source", html);
        Assert.Contains("Use as reference only", html);
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
        Assert.Contains("saveConversionSourceAttachment(file.name, source)", script);
        Assert.Contains("sources: [source]", script);
        Assert.Contains("One source file · add conversion instructions below", script);
        Assert.DoesNotContain("promptInput.value = request;", script);
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

        Assert.Contains("request.Sources.Count > 4", endpoint);
        Assert.Contains("no more than four source files", endpoint);
        Assert.Contains("Conversion tasks allow one source file", endpoint);
    }

    [Fact]
    public void ResponseActionsRequireACompleteNinjaScriptFile()
    {
        var script = File.ReadAllText(Path.Combine(Root, "wwwroot", "js", "workspace.js"));

        Assert.Contains("if (looksLikeCompleteNinjaScript(code))", script);
        Assert.Contains("generatedCode.push(code)", script);
        Assert.DoesNotContain(
            "appendCodeBlock(container, code);\n        generatedCode.push(code);",
            script.Replace("\r\n", "\n"));
    }
}
