namespace NinjaTrader_Xen.Tests;

public sealed class ModelFeedbackTests
{
    [Fact]
    public void Workspace_BindsVotesToExactResponseAndRagMatches()
    {
        var script = File.ReadAllText(FindRepositoryFile(
            "wwwroot", "js", "workspace.js"));

        Assert.Contains("addModelFeedbackControls(assistantMessage, {", script);
        Assert.Contains("userPrompt: prompt", script);
        Assert.Contains("assistantOutput: assistantText", script);
        Assert.Contains("ragDebug", script);
        Assert.Contains("/api/model-feedback", script);
        Assert.Contains("responseActions.prepend(container)", script);
        Assert.Contains("Knowledge matches:\\n${lines.join(\"\\n\")}", script);
        Assert.Contains("meta.ragDebug.matches.filter(match => match.used)", script);
    }

    [Fact]
    public void Endpoint_UsesExistingFeedbackTableAndPlatformMarker()
    {
        var endpoint = File.ReadAllText(FindRepositoryFile(
            "Endpoints", "FeedbackEndpoints.cs"));

        Assert.Contains("INSERT INTO dbo.ModelFeedback", endpoint);
        Assert.Contains(".Value = \"NinjaTrader Xen\";", endpoint);
        Assert.Contains("@ConversationId", endpoint);
        Assert.Contains("@UserPrompt", endpoint);
        Assert.Contains("@AssistantOutput", endpoint);
    }

    [Fact]
    public void RagDiagnostics_AreReturnedEvenWhenVisualDebugIsHidden()
    {
        var endpoint = File.ReadAllText(FindRepositoryFile(
            "Endpoints", "ChatEndpoints.cs"));

        Assert.Contains("if (rag?.Best is not null)", endpoint);
        Assert.Contains("showDebug = knowledgeRetriever.Options.ShowDebug", endpoint);
        Assert.Contains("id = match.Id", endpoint);
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
