using System.Runtime.CompilerServices;

namespace NinjaTrader_Xen.Tests;

public sealed class PromptBuilderRetrievalContextTests
{
    [Fact]
    public void Workspace_PreservesOriginalPromptForBuildPlanRetrieval()
    {
        var root = GetProjectRoot();
        var script = File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "js",
            "workspace.js"));

        Assert.Contains("originalPrompt: originalPrompt?.trim()", script);
        Assert.Contains("retrievalPrompt: buildRetrievalPrompt(prompt)", script);
        Assert.Contains("Current Build Plan step:", script);
    }

    [Fact]
    public void Workspace_BlocksDuplicateSubmissionWhilePromptBuilderRuns()
    {
        var script = ReadWorkspaceScript();

        Assert.Contains("if (generating || preparingRequest)", script);
        Assert.Contains("preparingRequest = true;", script);
        Assert.Contains("preparingRequest = false;", script);
    }

    [Fact]
    public void Workspace_ReviewsEveryBuildMessageNotOnlyEmptyProjects()
    {
        var script = ReadWorkspaceScript();
        var start = script.IndexOf(
            "async function reviewBuildPrompt",
            StringComparison.Ordinal);
        var end = script.IndexOf(
            "function showPromptReview",
            start,
            StringComparison.Ordinal);
        var reviewFunction = script[start..end];

        Assert.Contains("/api/prompt-builder/check", reviewFunction);
        Assert.DoesNotContain("currentProjectId ||", reviewFunction);
        Assert.DoesNotContain("history.length > 0", reviewFunction);
        Assert.DoesNotContain("promptQualityChecked", script);
        Assert.Contains("promptReviewCompleted: completedPromptReview", script);
        Assert.Contains("hasCurrentCode: Boolean(getLatestGeneratedCode())", script);
        Assert.Contains(
            "previousAssistantResponse: getLatestHistoryContent(\"assistant\")",
            script);
    }

    [Fact]
    public void Workspace_SuggestsEditableBaselinesOnlyForBlankAnswers()
    {
        var script = ReadWorkspaceScript();

        Assert.Contains("Let Xen suggest baseline answers", script);
        Assert.Contains("promptBuilderBody.prepend(row)", script);
        Assert.DoesNotContain("promptBuilderActions.insertBefore", script);
        Assert.Contains("/api/prompt-builder/suggestions", script);
        Assert.Contains(".filter(field => !field.value.trim())", script);
        Assert.Contains("if (field.value.trim())", script);
        Assert.Contains("review and edit before creating the plan", script);
    }

    [Fact]
    public void Workspace_ScopesBuildPlanToTheActiveProject()
    {
        var script = ReadWorkspaceScript();

        Assert.Contains("projectId: currentProjectId", script);
        Assert.Contains("plan.projectId !== currentProjectId", script);
        Assert.Contains("plan.projectId = currentProjectId", script);
    }

    [Fact]
    public void Workspace_ClearTaskPreservesTheSelectedTask()
    {
        var script = ReadWorkspaceScript();

        Assert.Contains("startNewProject(false);", script);
        Assert.Contains("if (history.length && !window.confirm(", script);
    }

    [Fact]
    public void Workspace_ClearTaskIsInTheComposerNotTheHeader()
    {
        var root = GetProjectRoot();
        var markup = File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "workspace.html"));
        var header = markup[markup.IndexOf("<header", StringComparison.Ordinal)..
            markup.IndexOf("</header>", StringComparison.Ordinal)];
        var composer = markup[markup.IndexOf("<form id=\"chatForm\"", StringComparison.Ordinal)..
            markup.IndexOf("</form>", StringComparison.Ordinal)];

        Assert.DoesNotContain("newProjectButton", header);
        Assert.Contains("newProjectButton", composer);
        Assert.Contains("composer-context-actions", composer);
    }

    [Fact]
    public void Workspace_UserPromptHasAnAccessibleDistinctSurface()
    {
        var root = GetProjectRoot();
        var styles = File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "css",
            "site.css"));

        Assert.Contains("background: #303030;", styles);
        Assert.Contains("color: #f4f4f4;", styles);
        Assert.Contains("font-size: 15px;", styles);
        Assert.Contains("background: #e8ebef;", styles);
    }

    [Fact]
    public void Workspace_AutomaticallyBuildChecksCompletedBuildPlanSteps()
    {
        var script = ReadWorkspaceScript();

        Assert.Contains("const automaticBuildResult = generatedCode", script);
        Assert.Contains("{ automatic: true }", script);
        Assert.Contains("setBuildPlanStepStatus(\"build-checking\")", script);
        Assert.Contains(
            "result.success ? \"compiled\" : \"build-failed\"",
            script);
        Assert.Contains("Continue anyway to Prompt", script);
    }

    [Fact]
    public void Workspace_AutomaticallyBuildChecksGeneratedRepairs()
    {
        var script = ReadWorkspaceScript();

        Assert.Contains(
            "\"Repair the latest complete NinjaScript source\"",
            script);
        Assert.Contains(
            "buildPlanStepReady || isGeneratedRepair",
            script);
        Assert.Contains(
            "isGeneratedRepair && !generatedCode",
            script);
        Assert.Contains(
            "no complete C# file returned for Build Check",
            script);
    }

    [Fact]
    public void Workspace_ClarificationResponseDoesNotCompleteBuildPlanStep()
    {
        var script = ReadWorkspaceScript();

        Assert.Contains("extractLatestCodeBlock(assistantText)", script);
        Assert.Contains(
            "setBuildPlanStepStatus(\"awaiting-clarification\")",
            script);
        Assert.Contains(
            "plan.stepStatus === \"awaiting-clarification\"",
            script);
        Assert.Contains("Waiting for your answer", script);
    }

    [Fact]
    public void Workspace_RestoringSourceClearsTheActiveBuildPlan()
    {
        var script = ReadWorkspaceScript();
        var restoreStart = script.IndexOf(
            "async function restoreCodeRevision",
            StringComparison.Ordinal);
        var restoreEnd = script.IndexOf(
            "function setCodeWorkspaceCode",
            restoreStart,
            StringComparison.Ordinal);
        var restoreFunction = script[restoreStart..restoreEnd];

        Assert.Contains("applyProjectToWorkspace(result);", restoreFunction);
        Assert.Contains("clearBuildPlan();", restoreFunction);
        Assert.True(
            restoreFunction.IndexOf("applyProjectToWorkspace(result);", StringComparison.Ordinal) <
            restoreFunction.IndexOf("clearBuildPlan();", StringComparison.Ordinal));
        Assert.Contains(
            "The active Build Plan was cleared because it referred to a newer source state.",
            restoreFunction);
    }

    private static string ReadWorkspaceScript()
    {
        var root = GetProjectRoot();
        return File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "js",
            "workspace.js"));
    }

    private static string GetProjectRoot(
        [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            ".."));
}
