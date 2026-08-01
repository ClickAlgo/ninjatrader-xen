using System.Runtime.CompilerServices;

namespace NinjaTrader_Xen.Tests;

public sealed class FeedbackInvitationTests
{
    [Fact]
    public void Workspace_ReusesFeedbackFormForFrustratedUsers()
    {
        var root = GetProjectRoot();
        var script = File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "js",
            "workspace.js"));

        Assert.Contains("function isClearlyFrustrated(prompt)", script);
        Assert.Contains("appendFeedbackInvitation(assistantMessage)", script);
        Assert.Contains("openFeedback({ promptedByFrustration: true })", script);
        Assert.Contains("nx_feedback_invitation_", script);
        Assert.Contains("feedbackIncludeDiagnostics", script);
    }

    private static string GetProjectRoot(
        [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            ".."));
}
