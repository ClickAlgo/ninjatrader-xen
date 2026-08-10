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
    public void ExistingCodeUiGateNamesOnlyTheTwoExistingTasks()
    {
        var script = File.ReadAllText(Path.Combine(Root, "wwwroot", "js", "workspace.js"));
        const string expected = "return task === \"existing-strategy\" || task === \"existing-indicator\";";

        Assert.Contains(expected, script);
    }

    [Fact]
    public void DatabaseConstraintLimitsStateToExistingTasks()
    {
        var migration = File.ReadAllText(Path.Combine(
            Root, "Database", "002-existing-code-project-state.sql"));

        Assert.Contains("CHECK (Task IN (N'existing-strategy', N'existing-indicator'))", migration);
        Assert.DoesNotContain("build-strategy", migration);
        Assert.DoesNotContain("convert-strategy", migration);
    }

    [Fact]
    public void ExistingCodeEndpointCapsSourceBundlesAtFour()
    {
        var endpoint = File.ReadAllText(Path.Combine(
            Root, "Endpoints", "ExistingCodeEndpoints.cs"));

        Assert.Contains("request.Sources.Count > 4", endpoint);
        Assert.Contains("no more than four source files", endpoint);
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
