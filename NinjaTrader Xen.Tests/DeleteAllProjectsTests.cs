namespace NinjaTrader_Xen.Tests;

public sealed class DeleteAllProjectsTests
{
    [Fact]
    public void Endpoint_DeletesSubscriberProjectDataInOneTransaction()
    {
        var endpoint = File.ReadAllText(FindRepositoryFile(
            "Endpoints", "ProjectEndpoints.cs"));

        Assert.Contains("group.MapDelete(\"/all\", DeleteAll)", endpoint);
        Assert.Contains("private static async Task<IResult> DeleteAll(", endpoint);
        Assert.Contains("DELETE FROM dbo.ExistingCodeProjectStates", endpoint);
        Assert.Contains("DELETE FROM dbo.ProjectMemoryTurns", endpoint);
        Assert.Contains("DELETE FROM dbo.ProjectRevisions", endpoint);
        Assert.Contains("DELETE FROM dbo.SavedConversations", endpoint);
        Assert.Contains("WHERE SubscriberId = @SubscriberId", endpoint);
        Assert.Contains("await transaction.CommitAsync()", endpoint);
    }

    [Fact]
    public void Workspace_RequiresTypedConfirmationBeforeBulkDelete()
    {
        var script = File.ReadAllText(FindRepositoryFile(
            "wwwroot", "js", "workspace.js"));

        Assert.Contains("async function deleteAllProjects()", script);
        Assert.Contains(
            "confirmation?.trim().toUpperCase() !== \"DELETE\"",
            script);
        Assert.Contains("/api/projects/all", script);
        Assert.Contains("startNewProject(false)", script);
        Assert.Contains("await openProjects()", script);
    }

    [Fact]
    public void ProjectsModal_ProvidesClearlyNamedBulkDeleteControl()
    {
        var html = File.ReadAllText(FindRepositoryFile(
            "wwwroot", "workspace.html"));

        Assert.Contains("id=\"projectsFooter\"", html);
        Assert.Contains("id=\"deleteAllProjectsButton\"", html);
        Assert.Contains("Delete all projects", html);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            string.Join(Path.DirectorySeparatorChar, segments));
    }
}
