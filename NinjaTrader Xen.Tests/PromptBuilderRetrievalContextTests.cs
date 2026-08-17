using System.Runtime.CompilerServices;
using NinjaTrader_Xen.Services;

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
    public void Workspace_UsesPromptBuilderUntilCompleteCodeExists()
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
        Assert.Contains(
            "looksLikeCompleteNinjaScript(getLatestGeneratedCode())",
            reviewFunction);
        Assert.DoesNotContain(
            "currentProjectId || history.length > 0",
            reviewFunction);
        Assert.DoesNotContain("promptQualityChecked", script);
        Assert.Contains("promptReviewCompleted: completedPromptReview", script);
        Assert.Contains("hasCurrentCode: Boolean(getLatestGeneratedCode())", script);
        Assert.Contains(
            "previousAssistantResponse: getLatestHistoryContent(\"assistant\")",
            script);
    }

    [Fact]
    public void PromptBuilderService_RejectsCurrentCodeBeforeComplexityRouting()
    {
        var root = GetProjectRoot();
        var service = File.ReadAllText(Path.Combine(
            root,
            "Services",
            "PromptBuilderService.cs"));
        var currentCodeGuard = service.IndexOf(
            "if (hasCurrentCode)",
            StringComparison.Ordinal);
        var complexityCheck = service.IndexOf(
            "TradingRequestComplexityPolicy.Evaluate",
            StringComparison.Ordinal);

        Assert.True(currentCodeGuard >= 0);
        Assert.True(currentCodeGuard < complexityCheck);
        Assert.Contains("return new(false, \"\", false);", service);
    }

    [Theory]
    [InlineData("build a strategy")]
    [InlineData("Create an automated trading system")]
    [InlineData("Develop automated order management")]
    public void PromptBuilderService_StopsStrategyRequestsInIndicatorTask(string prompt)
    {
        Assert.True(PromptBuilderService.IsStrategyRequestInIndicatorTask(
            "build-indicator",
            prompt));
    }

    [Theory]
    [InlineData("Build an indicator with buy and sell chart markers")]
    [InlineData("Create an RSI indicator with alerts")]
    [InlineData("Build an indicator with no automated trade execution")]
    [InlineData("Build an indicator that never manages orders or positions")]
    public void PromptBuilderService_AllowsIndicatorSignalsAndAlerts(string prompt)
    {
        Assert.False(PromptBuilderService.IsStrategyRequestInIndicatorTask(
            "build-indicator",
            prompt));
    }

    [Theory]
    [InlineData("build an indicator")]
    [InlineData("Create a custom technical indicator")]
    [InlineData("Develop a chart study")]
    public void PromptBuilderService_StopsIndicatorRequestsInStrategyTask(string prompt)
    {
        Assert.True(PromptBuilderService.IsIndicatorRequestInStrategyTask(
            "build-strategy",
            prompt));
    }

    [Theory]
    [InlineData("Build a strategy using an RSI indicator")]
    [InlineData("Create a strategy with indicator-based entries")]
    [InlineData("Build a strategy and create an indicator panel for its signals")]
    public void PromptBuilderService_AllowsStrategiesThatUseIndicators(string prompt)
    {
        Assert.False(PromptBuilderService.IsIndicatorRequestInStrategyTask(
            "build-strategy",
            prompt));
    }

    [Theory]
    [InlineData("build-strategy", "can you build NinjaTrader strategies?")]
    [InlineData("build-indicator", "Can Xen build NinjaTrader 8 indicators")]
    [InlineData("build-strategy", "What can you build in NinjaScript?")]
    public void PromptBuilderService_BypassesCapabilityQuestions(
        string task,
        string prompt)
    {
        Assert.True(PromptBuilderService.IsNewBuildCapabilityQuestion(
            task,
            prompt));
    }

    [Theory]
    [InlineData("Can you build me a strategy?")]
    [InlineData("Can you build a strategy that trades an EMA crossover?")]
    [InlineData("Can you build me an RSI indicator?")]
    [InlineData("Build a NinjaTrader strategy")]
    public void PromptBuilderService_DoesNotBypassBuildRequests(string prompt)
    {
        Assert.False(PromptBuilderService.IsNewBuildCapabilityQuestion(
            "build-strategy",
            prompt));
    }

    [Fact]
    public void Workspace_ShowsTaskTypeGuardBeforePromptBuilderQuestions()
    {
        var script = ReadWorkspaceScript();
        var reviewStart = script.IndexOf(
            "async function reviewBuildPrompt",
            StringComparison.Ordinal);
        var reviewEnd = script.IndexOf(
            "function showPromptReview",
            reviewStart,
            StringComparison.Ordinal);
        var reviewFunction = script[reviewStart..reviewEnd];

        Assert.Contains("if (response.stopMessage)", reviewFunction);
        Assert.Contains(
            "const notice = addMessage(\"assistant\", response.stopMessage)",
            reviewFunction);
        Assert.Contains(
            "appendTaskSwitchAction(notice, targetTask, prompt)",
            reviewFunction);
        Assert.DoesNotContain("history.push", reviewFunction[
            reviewFunction.IndexOf("if (response.stopMessage)", StringComparison.Ordinal)..
            reviewFunction.IndexOf("if (!response.recommendPromptBuilder)", StringComparison.Ordinal)]);
        Assert.True(
            reviewFunction.IndexOf("if (response.stopMessage)", StringComparison.Ordinal) <
            reviewFunction.IndexOf("showPromptReview", StringComparison.Ordinal));
    }

    [Fact]
    public void Workspace_TaskMismatchSwitchCarriesTheOriginalPrompt()
    {
        var script = ReadWorkspaceScript();

        Assert.Contains("function appendTaskSwitchAction", script);
        Assert.Contains("switchToMismatchTask(targetTask)", script);
        Assert.Contains("promptInput.value = originalPrompt;", script);
        Assert.Contains("Switch to ${taskNames[targetTask]}", script);
    }

    [Fact]
    public void Workspace_AddsTaskSwitchToChatGeneratedMismatchWarning()
    {
        var script = ReadWorkspaceScript();

        Assert.Contains(
            "getTaskSwitchTargetFromResponse(assistantText)",
            script);
        Assert.Contains(
            "appendTaskSwitchAction(assistantMessage, responseSwitchTarget, prompt)",
            script);
        Assert.Contains(
            "Please select the Build Strategy task and enter your request there.",
            script);
        Assert.Contains(
            "Please select the Build Indicator task and enter your request there.",
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
    public void Workspace_SnapshotsCodeWhenAutomaticBuildReportIsNewest()
    {
        var script = ReadWorkspaceScript();
        var saveStart = script.IndexOf(
            "async function saveCurrentProject(code = \"\")",
            StringComparison.Ordinal);
        var saveEnd = script.IndexOf(
            "async function openCodeWorkspace()",
            saveStart,
            StringComparison.Ordinal);
        var saveFunction = script[saveStart..saveEnd];

        Assert.Contains("async function saveCurrentProject(code = \"\")", saveFunction);
        Assert.Contains("looksLikeCompleteNinjaScript(code)", saveFunction);
        Assert.Contains("if (isNewCodeBuildTask() && !latestCode)", saveFunction);
        Assert.Contains("latestCode: latestCode || null", saveFunction);
        Assert.DoesNotContain("latestAssistant", saveFunction);
    }

    [Fact]
    public void Workspace_TreatsUploadedSourceAsCurrentProjectCode()
    {
        var script = ReadWorkspaceScript();
        var functionStart = script.IndexOf(
            "function getLatestGeneratedCode()",
            StringComparison.Ordinal);
        var functionEnd = script.IndexOf(
            "function extractLatestCodeBlock(text)",
            functionStart,
            StringComparison.Ordinal);
        var function = script[functionStart..functionEnd];

        Assert.Contains("turn.role === \"user\"", function);
        Assert.Contains("Source file:", function);
        Assert.Contains("uploadedSource[1].trim()", function);
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
    public void Workspace_CanExitBuildPlanWithoutClearingSavedWork()
    {
        var script = ReadWorkspaceScript();
        var exitStart = script.IndexOf(
            "function exitBuildPlan()",
            StringComparison.Ordinal);
        var exitEnd = script.IndexOf(
            "function renderActiveBuildPlan()",
            exitStart,
            StringComparison.Ordinal);
        var exitFunction = script[exitStart..exitEnd];

        Assert.Contains("Exit this Build Plan?", exitFunction);
        Assert.Contains("clearBuildPlan();", exitFunction);
        Assert.Contains("conversation history will remain saved", exitFunction);
        Assert.Contains("plan.stepStatus === \"loaded\"", exitFunction);
        Assert.DoesNotContain("startNewProject", exitFunction);
        Assert.Contains("exit.textContent = \"Exit plan\"", script);
    }

    [Fact]
    public void Workspace_TaskChangeWarnsAndClearsActiveBuildPlan()
    {
        var script = ReadWorkspaceScript();
        var handlerStart = script.IndexOf(
            "document.getElementById(\"taskButtons\").addEventListener",
            StringComparison.Ordinal);
        var handlerEnd = script.IndexOf(
            "form.addEventListener(\"submit\"",
            handlerStart,
            StringComparison.Ordinal);
        var handler = script[handlerStart..handlerEnd];

        Assert.Contains("const activePlan = getBuildPlan();", handler);
        Assert.Contains("The active Build Plan", handler);
        Assert.Contains("window.confirm(warning)", handler);
        Assert.Contains("clearBuildPlan();", handler);
        Assert.True(
            handler.IndexOf("window.confirm(warning)", StringComparison.Ordinal) <
            handler.IndexOf("clearBuildPlan();", StringComparison.Ordinal));
        Assert.True(
            handler.IndexOf("clearBuildPlan();", StringComparison.Ordinal) <
            handler.IndexOf("activeTask = nextTask;", StringComparison.Ordinal));
        Assert.Contains("Saved projects and snapshots", handler);
    }

    [Fact]
    public void Workspace_RedirectsMismatchedExistingNinjaScriptBeforeChat()
    {
        var script = ReadWorkspaceScript();
        var submitStart = script.IndexOf(
            "form.addEventListener(\"submit\"",
            StringComparison.Ordinal);
        var chatRequest = script.IndexOf(
            "fetch(\"/api/chat/stream\"",
            submitStart,
            StringComparison.Ordinal);
        var redirectCheck = script.IndexOf(
            "redirectMismatchedExistingSource(prompt)",
            submitStart,
            StringComparison.Ordinal);

        Assert.True(redirectCheck > submitStart);
        Assert.True(redirectCheck < chatRequest);
        Assert.Contains("(Strategy|Indicator)", script);
        Assert.Contains(
            "? \"existing-strategy\"\n        : \"existing-indicator\"",
            script);
        Assert.Contains("startNewProject(false);", script);
        Assert.Contains("correct specialist prompt is used", script);
        Assert.Contains(
            "redirectMismatchedExistingSource(request, file.name)",
            script);
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
