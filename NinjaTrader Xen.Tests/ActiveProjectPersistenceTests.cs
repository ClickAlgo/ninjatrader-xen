using System.Runtime.CompilerServices;

namespace NinjaTrader_Xen.Tests;

public sealed class ActiveProjectPersistenceTests
{
    [Fact]
    public void Workspace_RestoresOnlyTheActiveSavedProjectIdentity()
    {
        var script = ReadProjectFile("wwwroot", "js", "workspace.js");
        var restore = FunctionBody(script, "async function restoreActiveProject()");

        Assert.Contains(
            "const activeProjectStorageKey = \"nx_active_saved_project_id\";",
            script);
        Assert.Contains("restoreActiveProject();", script);
        Assert.Contains("sessionStorage.getItem(activeProjectStorageKey)", restore);
        Assert.Contains("fetch(`/api/projects/${projectId}`", restore);
        Assert.Contains("applyProjectToWorkspace(project);", restore);
        Assert.Contains("await loadExistingCodeState();", restore);
        Assert.DoesNotContain("localStorage.setItem(activeProjectStorageKey", script);
    }

    [Fact]
    public void Workspace_SetsIdentityOnlyAfterSaveOrExplicitOpen()
    {
        var script = ReadProjectFile("wwwroot", "js", "workspace.js");
        var save = FunctionBody(script, "async function saveCurrentProject(code = \"\")");
        var open = FunctionBody(script, "async function loadProject(projectId)");

        Assert.True(
            save.IndexOf("if (response.ok)", StringComparison.Ordinal) <
            save.IndexOf("sessionStorage.setItem(activeProjectStorageKey, currentProjectId)",
                StringComparison.Ordinal));
        Assert.True(
            open.IndexOf("applyProjectToWorkspace(project);", StringComparison.Ordinal) <
            open.IndexOf("sessionStorage.setItem(activeProjectStorageKey, project.projectId)",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Workspace_ClearsIdentityOnlyForExplicitResetOrConfirmedNotFound()
    {
        var script = ReadProjectFile("wwwroot", "js", "workspace.js");
        var startNew = FunctionBody(script, "function startNewProject(resetTask = true)");
        var restore = FunctionBody(script, "async function restoreActiveProject()");

        Assert.Contains("sessionStorage.removeItem(activeProjectStorageKey);", startNew);
        Assert.Contains("if (response.status === 404)", restore);
        Assert.Contains("sessionStorage.removeItem(activeProjectStorageKey);", restore);
        Assert.Equal(2, CountOccurrences(
            script, "sessionStorage.removeItem(activeProjectStorageKey);"));
        Assert.DoesNotContain("activeProjectStorageKey", ReadProjectFile(
            "wwwroot", "js", "account.js"));
        Assert.DoesNotContain("activeProjectStorageKey", ReadProjectFile(
            "wwwroot", "js", "topup.js"));
        Assert.DoesNotContain("activeProjectStorageKey", ReadProjectFile(
            "wwwroot", "js", "payment-complete.js"));
    }

    [Fact]
    public void Workspace_ServesTheUpdatedScriptVersion()
    {
        var html = ReadProjectFile("wwwroot", "workspace.html");

        Assert.Contains("/js/workspace.js?v=1.1.36", html);
    }

    private static string FunctionBody(string script, string declaration)
    {
        var start = script.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing function: {declaration}");
        var next = script.IndexOf("\nfunction ", start + declaration.Length,
            StringComparison.Ordinal);
        var nextAsync = script.IndexOf("\nasync function ",
            start + declaration.Length, StringComparison.Ordinal);
        var end = new[] { next, nextAsync }.Where(index => index >= 0)
            .DefaultIfEmpty(script.Length).Min();
        return script[start..end];
    }

    private static int CountOccurrences(string value, string fragment) =>
        (value.Length - value.Replace(fragment, "", StringComparison.Ordinal).Length) /
        fragment.Length;

    private static string ReadProjectFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([GetProjectRoot(), .. parts]));

    private static string GetProjectRoot(
        [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, ".."));
}
