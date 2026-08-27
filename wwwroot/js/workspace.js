const token = sessionStorage.getItem("nx_access_token");
if (!token)
    location.replace("/login.html");

const taskNames = {
    "build-strategy": "Build Strategy",
    "build-indicator": "Build Indicator",
    "existing-strategy": "Existing Strategy",
    "existing-indicator": "Existing Indicator",
    "convert-strategy": "Convert Strategy",
    "convert-indicator": "Convert Indicator",
    "analyse-backtest": "Analyse Backtest"
};

const taskPlaceholders = {
    "build-strategy": "Describe your NinjaTrader strategy…",
    "build-indicator": "Describe your NinjaTrader indicator…",
    "existing-strategy": "Paste your strategy code and describe the changes…",
    "existing-indicator": "Paste your indicator code and describe the changes…",
    "convert-strategy": "Paste strategy source from another platform or upload a file…",
    "convert-indicator": "Paste indicator source from another platform or upload a file…",
    "analyse-backtest": "Add the instrument, timeframe and any test context…"
};
const BACKTEST_REPORT_INTRODUCTION =
    "This report reviews the supplied NinjaTrader Strategy Analyzer exports " +
    "to explain performance, risk, trade behaviour and robustness. It helps " +
    "identify weaknesses, missing assumptions and useful next tests before " +
    "considering simulation or live use. It is not a profitability forecast " +
    "or trading recommendation.";

let activeTask = "build-strategy";
let history = [];
let generating = false;
let preparingRequest = false;
let currentProjectId = null;
let currentProjectTitle = "";
let currentProjectPersisted = false;
let hasProjectSnapshots = false;
let currentController = null;
let currentBalanceGbp = null;
let promptBuilderBypassed = false;
let promptReviewCompleted = false;
let pendingImage = null;
let pendingAnalyzerExports = [];
let existingCodeState = { sources: [], decisions: [], workingCode: null };
let acceptedModelSelection = "";
let preflightBuilding = false;
let revisionPage = 1;
let revisionTotalPages = 1;
const revisionPageSize = 20;

const lowCreditThresholdGbp = 1;
const activeProjectStorageKey = "nx_active_saved_project_id";
const buildPlanStorageKey = "nx_active_build_plan_v1";
const mobileWorkspaceNoticeKey = "nx_mobile_workspace_notice_dismissed";
const defaultModel = "gpt-5.3-codex";
const preflightRepairPromptPrefix =
    "Repair the latest complete NinjaScript source so it passes";
const generatedRepairPromptPrefix =
    "Repair the latest complete NinjaScript source";
const lowCostModels = new Set([
    "gpt-5.6-luna",
    "deepseek-v4-pro",
    "kimi-k2.7-code"
]);
const imageUnsupportedModels = new Set([
    "gpt-5.3-codex",
    "deepseek-v4-pro",
    "kimi-k2.7-code"
]);

const messages = document.getElementById("messages");
const form = document.getElementById("chatForm");
const promptInput = document.getElementById("promptInput");
const sendButton = document.getElementById("sendButton");
const clearInputButton = document.getElementById("clearInputButton");
const cancelButton = document.getElementById("cancelButton");
const status = document.getElementById("chatStatus");
const modelSelect = document.getElementById("modelSelect");
const modelCostBadge = document.getElementById("modelCostBadge");
const projectsModal = document.getElementById("projectsModal");
const projectsList = document.getElementById("projectsList");
const promptBuilderModal = document.getElementById("promptBuilderModal");
const promptBuilderBody = document.getElementById("promptBuilderBody");
const promptBuilderActions = document.getElementById("promptBuilderActions");
const promptBuilderStatus = document.getElementById("promptBuilderStatus");
const promptBuilderReason = document.getElementById("promptBuilderReason");
const activeBuildPlanPanel = document.getElementById("activeBuildPlanPanel");
const feedbackModal = document.getElementById("feedbackModal");
const feedbackForm = document.getElementById("feedbackForm");
const feedbackComment = document.getElementById("feedbackComment");
const feedbackStatus = document.getElementById("feedbackStatus");
const submitFeedbackButton = document.getElementById("submitFeedbackButton");
const codeWorkspaceModal = document.getElementById("codeWorkspaceModal");
const codeViewButton = document.getElementById("codeViewButton");
const codeWorkspacePreview = document.getElementById("codeWorkspacePreview");
const codeWorkspacePrompt = document.getElementById("codeWorkspacePrompt");
const revisionList = document.getElementById("revisionList");
const revisionMessage = document.getElementById("revisionMessage");
const sourceImport = document.getElementById("sourceImport");
const sourceFileInput = document.getElementById("sourceFileInput");
const sourceFileButton = document.getElementById("sourceFileButton");
const sourceFileStatus = document.getElementById("sourceFileStatus");
const analyzerTradesFileInput =
    document.getElementById("analyzerTradesFileInput");
const analyzerTradesFileButton =
    document.getElementById("analyzerTradesFileButton");
const existingCodeAttachments = document.getElementById("existingCodeAttachments");
const existingCodeModal = document.getElementById("existingCodeModal");
const existingCodeForm = document.getElementById("existingCodeForm");
const existingCodeText = document.getElementById("existingCodeText");
const existingCodeFileName = document.getElementById("existingCodeFileName");
const existingCodeRole = document.getElementById("existingCodeRole");
const existingCodeStatus = document.getElementById("existingCodeStatus");
const analyzerExportGuide = document.getElementById("analyzerExportGuide");
const analyzerAttachments =
    document.getElementById("analyzerAttachments");
const imageImport = document.getElementById("imageImport");
const imageFileInput = document.getElementById("imageFileInput");
const imageFileButton = document.getElementById("imageFileButton");
const imageFileStatus = document.getElementById("imageFileStatus");
const imagePreview = document.getElementById("imagePreview");
const imagePreviewContent = document.getElementById("imagePreviewContent");
const removeImageButton = document.getElementById("removeImageButton");
const themeToggleButton = document.getElementById("themeToggleButton");
const themeToggleIcon = document.getElementById("themeToggleIcon");
const themeToggleLabel = document.getElementById("themeToggleLabel");
const requirementsValidationModal =
    document.getElementById("requirementsValidationModal");
const requirementsValidationForm =
    document.getElementById("requirementsValidationForm");
const requirementsValidationText =
    document.getElementById("requirementsValidationText");
const requirementsValidationStatus =
    document.getElementById("requirementsValidationStatus");
const requirementsValidationModel =
    document.getElementById("requirementsValidationModel");
const runRequirementsValidationButton =
    document.getElementById("runRequirementsValidationButton");
let codeWorkspaceCode = "";

updateThemeToggle();
restoreSelectedModel();
updateModelCostBadge();
acceptedModelSelection = modelSelect.value;
modelSelect.addEventListener("change", handleModelChange);
promptInput.addEventListener("input", updateClearInputButton);
clearInputButton.addEventListener("click", clearComposerInput);
cancelButton.addEventListener("click", () => {
    if (!currentController)
        return;

    cancelButton.disabled = true;
    status.textContent = "Cancelling…";
    currentController.abort();
});
loadBalance();
showTrialWelcome();
showMobileWorkspaceNotice();
renderActiveBuildPlan();
updateTaskSpecificUi();
updateClearInputButton();
updateHistoryButton();
restoreActiveProject();
sourceFileButton.addEventListener("click", () => {
    if (isExistingCodeTask())
        openExistingCodeModal();
    else
        sourceFileInput.click();
});
sourceFileInput.addEventListener("change", importSourceFile);
analyzerTradesFileButton.addEventListener("click", () =>
    analyzerTradesFileInput.click());
analyzerTradesFileInput.addEventListener("change", importAnalyzerTradesFile);
existingCodeForm.addEventListener("submit", saveExistingCodeAttachment);
document.getElementById("closeExistingCodeButton").addEventListener("click", closeExistingCodeModal);
document.getElementById("cancelExistingCodeButton").addEventListener("click", closeExistingCodeModal);
document.getElementById("uploadExistingCodeButton").addEventListener("click", () => sourceFileInput.click());
existingCodeModal.addEventListener("click", event => {
    if (event.target === existingCodeModal) closeExistingCodeModal();
});
imageFileButton.addEventListener("click", () => imageFileInput.click());
imageFileInput.addEventListener("change", importReferenceImage);
removeImageButton.addEventListener("click", clearPendingImage);
themeToggleButton.addEventListener("click", toggleWorkspaceTheme);

document.getElementById("projectsButton").addEventListener("click", openProjects);
codeViewButton.addEventListener(
    "click",
    openCodeWorkspace);
document.getElementById("closeProjectsButton").addEventListener("click", closeProjects);
document.getElementById("deleteAllProjectsButton")
    .addEventListener("click", deleteAllProjects);
document.getElementById("closeCodeWorkspaceButton").addEventListener(
    "click",
    closeCodeWorkspace);
document.getElementById("copyWorkspaceCodeButton").addEventListener(
    "click",
    copyWorkspaceCode);
document.getElementById("downloadWorkspaceCodeButton").addEventListener(
    "click",
    () => {
        if (codeWorkspaceCode)
            downloadCode(codeWorkspaceCode);
    });
document.getElementById("previousRevisionPageButton").addEventListener(
    "click",
    () => loadCodeRevisionPage(revisionPage - 1));
document.getElementById("nextRevisionPageButton").addEventListener(
    "click",
    () => loadCodeRevisionPage(revisionPage + 1));
document.getElementById("deleteUnpinnedRevisionsButton").addEventListener(
    "click",
    deleteUnpinnedCodeRevisions);
codeWorkspaceModal.addEventListener("click", event => {
    if (event.target === codeWorkspaceModal)
        closeCodeWorkspace();
});
document.getElementById("newProjectButton").addEventListener("click", () => {
    if (generating || preparingRequest)
        return;
    if (history.length && !window.confirm(
        "Clear this task conversation? Your saved projects will remain available."))
        return;
    clearBuildPlan();
    startNewProject(false);
});
document.getElementById("feedbackButton").addEventListener(
    "click",
    openFeedback);
document.getElementById("closeFeedbackButton").addEventListener(
    "click",
    closeFeedback);
document.getElementById("cancelFeedbackButton").addEventListener(
    "click",
    closeFeedback);
feedbackModal.addEventListener("click", event => {
    if (event.target === feedbackModal)
        closeFeedback();
});
feedbackForm.addEventListener("submit", submitFeedback);
document.getElementById("closeRequirementsValidationButton")
    .addEventListener("click", closeRequirementsValidation);
document.getElementById("cancelRequirementsValidationButton")
    .addEventListener("click", closeRequirementsValidation);
requirementsValidationModal.addEventListener("click", event => {
    if (event.target === requirementsValidationModal)
        closeRequirementsValidation();
});
requirementsValidationForm.addEventListener(
    "submit",
    submitRequirementsValidation);

function toggleWorkspaceTheme() {
    const useLightTheme =
        document.documentElement.dataset.theme !== "light";

    if (useLightTheme) {
        document.documentElement.dataset.theme = "light";
        localStorage.setItem("nx_workspace_theme", "light");
    } else {
        delete document.documentElement.dataset.theme;
        localStorage.setItem("nx_workspace_theme", "dark");
    }

    updateThemeToggle();
}

function updateThemeToggle() {
    const isLight =
        document.documentElement.dataset.theme === "light";
    themeToggleButton.setAttribute("aria-pressed", String(isLight));
    themeToggleIcon.textContent = isLight ? "☾" : "☼";
    themeToggleLabel.textContent = isLight ? "Dark theme" : "Light theme";
}

projectsModal.addEventListener("click", event => {
    if (event.target === projectsModal)
        closeProjects();
});

promptBuilderModal.addEventListener("click", event => {
    if (event.target === promptBuilderModal)
        closePromptBuilder("edit");
});
document.getElementById("closePromptBuilderButton").addEventListener(
    "click",
    () => closePromptBuilder("edit"));

document.getElementById("taskButtons").addEventListener("click", event => {
    const button = event.target.closest("[data-task]");
    if (!button || generating || preparingRequest)
        return;

    const nextTask = button.dataset.task;
    if (nextTask === activeTask)
        return;

    const activePlan = getBuildPlan();
    const hasWorkspaceWork =
        Boolean(activePlan) ||
        Boolean(currentProjectId) ||
        history.length > 0 ||
        Boolean(promptInput.value.trim()) ||
        Boolean(pendingImage) ||
        pendingAnalyzerExports.length > 0;
    if (hasWorkspaceWork) {
        const warning = activePlan
            ? `Switch to ${taskNames[nextTask]}? The active Build Plan and current task workspace will be closed. Saved projects and snapshots will remain available.`
            : `Switch to ${taskNames[nextTask]}? The current task workspace will be closed. Saved projects and snapshots will remain available.`;
        if (!window.confirm(warning))
            return;
    }

    clearBuildPlan();
    activeTask = nextTask;
    document.querySelectorAll(".task-button").forEach(item =>
        item.classList.toggle("active", item === button));
    document.getElementById("taskTitle").textContent = taskNames[activeTask];
    promptInput.placeholder = taskPlaceholders[activeTask];
    startNewProject(false);
    updateTaskSpecificUi();
    applyCreditAvailability();
});

function switchToMismatchTask(nextTask) {
    const button = document.querySelector(
        `.task-button[data-task="${nextTask}"]`);
    if (!button)
        return false;

    clearBuildPlan();
    activeTask = nextTask;
    document.querySelectorAll(".task-button").forEach(item =>
        item.classList.toggle("active", item === button));
    document.getElementById("taskTitle").textContent = taskNames[activeTask];
    promptInput.placeholder = taskPlaceholders[activeTask];
    startNewProject(false);
    updateTaskSpecificUi();
    applyCreditAvailability();
    return true;
}

form.addEventListener("submit", async event => {
    event.preventDefault();
    if (generating || preparingRequest)
        return;

    if (currentBalanceGbp !== null && currentBalanceGbp <= 0) {
        applyCreditAvailability();
        status.textContent = "Credit exhausted · top up to continue";
        return;
    }

    let prompt = promptInput.value.trim();
    if (!prompt)
        return;
    if (isExistingCodeTask() && looksLikeCompleteNinjaScript(prompt)) {
        existingCodeText.value = prompt;
        existingCodeFileName.value = activeTask === "existing-strategy"
            ? "Strategy.cs" : "Indicator.cs";
        existingCodeStatus.textContent =
            "Full source is saved separately so it cannot be lost from chat history.";
        existingCodeModal.hidden = false;
        document.body.classList.add("modal-open");
        return;
    }
    if (isExistingCodeTask() && !hasCurrentExistingSource()) {
        status.textContent = "Add the current source code before sending requirements";
        openExistingCodeModal();
        return;
    }
    if (redirectMismatchedExistingSource(prompt))
        return;
    const shouldInviteFeedback = isClearlyFrustrated(prompt);

    const requestAnalyzerExports =
        activeTask === "analyse-backtest"
            ? pendingAnalyzerExports.map(item => ({ ...item }))
            : [];
    if (activeTask === "analyse-backtest" &&
        !requestAnalyzerExports.length &&
        !hasAnalyzerExportInHistory()) {
        status.textContent =
            "Upload the Strategy Analyzer Summary CSV first";
        sourceFileButton.focus();
        return;
    }
    if (requestAnalyzerExports.length &&
        !requestAnalyzerExports.some(item => item.summary)) {
        status.textContent =
            "A Strategy Analyzer Summary CSV is required";
        sourceFileButton.focus();
        return;
    }
    if (requestAnalyzerExports.length)
        prompt = buildAnalyzerRequest(prompt, requestAnalyzerExports);

    preparingRequest = true;
    promptBuilderBypassed = false;
    promptReviewCompleted = false;
    let reviewedPrompt;
    try {
        reviewedPrompt = await reviewBuildPrompt(prompt);
    } finally {
        preparingRequest = false;
    }
    if (!reviewedPrompt)
        return;
    prompt = reviewedPrompt;
    const isGeneratedRepair = isGeneratedRepairPrompt(prompt);

    const createdProjectForRequest = !currentProjectId;
    if (createdProjectForRequest) {
        currentProjectId = crypto.randomUUID();
        currentProjectTitle = createProjectTitle(
            isAnalyzerRequest(prompt)
                ? "Analyse Strategy Analyzer results"
                : prompt);
        updateProjectTitle();
    }

    const previousHistory = [...history];
    const requestImage = pendingImage;
    const userMessage = addMessage("user", prompt);
    if (requestImage)
        appendSubmittedImage(userMessage, requestImage);
    history.push({ role: "user", content: prompt });
    markBuildPlanPromptSent(prompt);
    promptInput.value = "";
    clearPendingImage();
    clearAnalyzerExports();
    updateClearInputButton();

    const assistantMessage = addMessage("assistant", "");
    const content = assistantMessage.querySelector(".message-content");
    assistantMessage.classList.add("generating");
    content.innerHTML = `
        <div class="working-indicator">
            <span class="working-dot"></span>
            <span class="working-dot"></span>
            <span class="working-dot"></span>
            <strong>${activeTask === "analyse-backtest"
                ? "Xen is analysing your backtest…"
                : "Xen is preparing your response…"}</strong>
        </div>
    `;

    generating = true;
    updateImageUploadUi();
    sendButton.disabled = true;
    sendButton.classList.add("loading");
    cancelButton.disabled = false;
    modelSelect.disabled = true;
    promptInput.disabled = true;
    status.textContent = "Working…";
    scrollMessagesToBottom();
    currentController = new AbortController();
    const bypassedPromptBuilder = promptBuilderBypassed;
    const completedPromptReview = promptReviewCompleted;
    promptBuilderBypassed = false;
    promptReviewCompleted = false;

    try {
        const response = await fetch("/api/chat/stream", {
            method: "POST",
            headers: {
                "Authorization": `Bearer ${token}`,
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                prompt,
                task: activeTask,
                model: modelSelect.value,
                history: previousHistory,
                image: requestImage,
                projectId: currentProjectId,
                retrievalPrompt: buildRetrievalPrompt(prompt),
                promptBuilderBypassed: bypassedPromptBuilder,
                promptReviewCompleted: completedPromptReview
            }),
            signal: currentController.signal
        });

        if (response.status === 401) {
            sessionStorage.removeItem("nx_access_token");
            location.replace("/login.html");
            return;
        }

        if (!response.ok || !response.body)
            throw new Error("The AI request could not be started.");

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = "";
        let assistantText = "";
        let ragDebug = null;

        while (true) {
            const { value, done } = await reader.read();
            if (done)
                break;

            buffer += decoder.decode(value, { stream: true });
            const frames = buffer.split("\n\n");
            buffer = frames.pop() || "";

            for (const frame of frames) {
                const line = frame.split("\n").find(item => item.startsWith("data:"));
                if (!line)
                    continue;

                const payload = line.slice(5).trim();
                if (payload === "[DONE]")
                    continue;

                const eventData = JSON.parse(payload);
                if (eventData.type === "response.output_text.delta") {
                    assistantText += eventData.delta;
                } else if (eventData.type === "rag.debug") {
                    ragDebug = eventData;
                } else if (eventData.type === "usage") {
                    updateBalance(eventData.balanceGbp);
                } else if (eventData.type === "blocked" || eventData.type === "error") {
                    if (eventData.type === "blocked" &&
                        eventData.balanceGbp !== undefined) {
                        updateBalance(eventData.balanceGbp);
                    }
                    throw new Error(eventData.message);
                }
            }
        }

        cancelButton.disabled = true;
        currentController = null;

        if (!assistantText)
            throw new Error("The AI returned an empty response.");

        history.push({ role: "assistant", content: assistantText });
        const responseCode = extractLatestCodeBlock(assistantText);
        const completeResponseCode = looksLikeCompleteNinjaScript(responseCode)
            ? responseCode
            : "";
        if (completeResponseCode && !currentProjectPersisted) {
            const titlePrompt = getBuildPlan()?.originalPrompt || prompt;
            currentProjectTitle = createProjectTitle(titlePrompt);
            updateProjectTitle();
        }
        const buildPlanStepReady = markBuildPlanResponseReady(prompt);
        assistantMessage.classList.remove("generating");
        if (activeTask === "analyse-backtest") {
            assistantMessage.classList.add("backtest-report-message");
            renderBacktestReport(content, assistantText);
        } else {
            renderStructuredResponse(content, assistantText);
        }
        const responseSwitchTarget = getTaskSwitchTargetFromResponse(assistantText);
        if (responseSwitchTarget)
            appendTaskSwitchAction(assistantMessage, responseSwitchTarget, prompt);
        if (ragDebug?.showDebug === true)
            appendRagDebug(assistantMessage, ragDebug);
        addModelFeedbackControls(assistantMessage, {
            conversationId: currentProjectId,
            model: modelSelect.value,
            task: activeTask,
            userPrompt: prompt,
            assistantOutput: assistantText,
            ragDebug
        });
        if (shouldInviteFeedback)
            appendFeedbackInvitation(assistantMessage);
        scrollMessagesToBottom();
        const shouldAutomaticallyBuild =
            buildPlanStepReady || isGeneratedRepair;
        const generatedCode = shouldAutomaticallyBuild
            ? completeResponseCode
            : "";
        const automaticBuildButton = generatedCode
            ? assistantMessage.querySelector(".preflight-build-button")
            : null;
        if (buildPlanStepReady && !generatedCode)
            setBuildPlanStepStatus("awaiting-clarification");
        const automaticBuildResult = generatedCode
            ? await runPreflightBuild(
                generatedCode,
                automaticBuildButton,
                { automatic: true })
            : undefined;
        if ((!generatedCode || automaticBuildResult === null) &&
            (completeResponseCode || !isNewCodeBuildTask())) {
            status.textContent = "Saving project…";
            const saved = await saveCurrentProject(completeResponseCode);
            status.textContent = isGeneratedRepair && !generatedCode
                ? (saved
                    ? "Repair saved · no complete C# file returned for Build Check"
                    : "Repair response ready · no complete C# file returned")
                : automaticBuildResult === null
                ? (saved
                    ? "Build Check unavailable · response saved"
                    : "Build Check unavailable · project not saved")
                : (saved ? "Saved" : "Response ready · project not saved");
        } else if (!completeResponseCode) {
            status.textContent = "Response ready";
        }
    } catch (error) {
        if (error.name === "AbortError") {
            history = previousHistory;
            userMessage.remove();
            assistantMessage.remove();
            promptInput.value = activeTask === "analyse-backtest"
                ? analyzerContextFromRequest(prompt)
                : prompt;
            if (requestAnalyzerExports.length) {
                pendingAnalyzerExports = requestAnalyzerExports;
                renderAnalyzerAttachments();
                sourceFileStatus.textContent =
                    `${requestAnalyzerExports.length} file${
                        requestAnalyzerExports.length === 1 ? "" : "s"} ready`;
            }
            if (requestImage)
                setPendingImage(requestImage);
            updateClearInputButton();
            restoreBuildPlanPromptLoaded(prompt);

            if (createdProjectForRequest) {
                currentProjectId = null;
                currentProjectTitle = "";
                updateProjectTitle();
            }

            status.textContent = "Cancelled";
            return;
        }

        restoreBuildPlanPromptLoaded(prompt);
        assistantMessage.classList.remove("generating");
        content.textContent = error.message;
        assistantMessage.classList.add("error");
        status.textContent = "Request failed";
    } finally {
        generating = false;
        sendButton.classList.remove("loading");
        cancelButton.disabled = true;
        modelSelect.disabled = false;
        currentController = null;
        applyCreditAvailability();
        updateImageUploadUi();
        renderExistingCodeAttachments();
        if (!promptInput.disabled)
            promptInput.focus();
    }
});

function addMessage(role, text) {
    const article = document.createElement("article");
    article.className = `message ${role}`;

    const label = document.createElement("div");
    label.className = "message-label";
    label.textContent = role === "user" ? "You" : "Xen";

    const content = document.createElement("div");
    content.className = "message-content";
    if (role === "user")
        renderUserMessage(content, text);
    else
        content.textContent = text;

    article.append(label, content);
    messages.appendChild(article);
    scrollMessagesToBottom();
    return article;
}

function appendTaskSwitchAction(message, targetTask, originalPrompt) {
    if (!targetTask || !taskNames[targetTask])
        return;

    const actions = document.createElement("div");
    actions.className = "task-switch-actions";
    const button = document.createElement("button");
    button.type = "button";
    button.className = "button primary";
    button.textContent = `Switch to ${taskNames[targetTask]}`;
    button.addEventListener("click", () => {
        if (!switchToMismatchTask(targetTask))
            return;
        promptInput.value = originalPrompt;
        updateClearInputButton();
        promptInput.focus();
    });
    actions.appendChild(button);
    message.querySelector(".message-content")?.appendChild(actions);
}

function getTaskSwitchTargetFromResponse(response) {
    const text = response?.trim() || "";
    if (text.includes(
        "Please select the Build Strategy task and enter your request there.")) {
        return "build-strategy";
    }
    if (text.includes(
        "Please select the Build Indicator task and enter your request there.")) {
        return "build-indicator";
    }
    return null;
}

function showTrialWelcome() {
    const stored = sessionStorage.getItem("nx_trial_welcome");
    if (!stored)
        return;

    sessionStorage.removeItem("nx_trial_welcome");

    try {
        const trial = JSON.parse(stored);
        const expires = new Date(trial.expiresUtc);
        if (Number.isNaN(expires.getTime()))
            return;

        const banner = document.createElement("aside");
        banner.className = "trial-welcome";

        const copy = document.createElement("div");
        const heading = document.createElement("strong");
        heading.textContent = "Welcome to Xen";
        const detail = document.createElement("p");
        detail.textContent =
            `We’ve added £5 free credit to your account. ` +
            `Your introductory credit is valid for 48 hours, until ${
                new Intl.DateTimeFormat("en-GB", {
                    dateStyle: "medium",
                    timeStyle: "short"
                }).format(expires)
            }.`;
        copy.append(heading, detail);

        const dismiss = document.createElement("button");
        dismiss.type = "button";
        dismiss.className = "trial-welcome-close";
        dismiss.setAttribute("aria-label", "Dismiss welcome message");
        dismiss.textContent = "×";
        dismiss.addEventListener("click", () => banner.remove());

        banner.append(copy, dismiss);
        messages.prepend(banner);
    } catch {
        // Ignore an invalid one-time welcome payload.
    }
}

function showNoTrialCreditNotice(subscriberId) {
    const seenKey = `nx_no_trial_credit_notice_${subscriberId}`;
    if (localStorage.getItem(seenKey) === "1")
        return;

    const banner = document.createElement("aside");
    banner.className = "trial-welcome trial-unavailable";

    const copy = document.createElement("div");
    const heading = document.createElement("strong");
    heading.textContent = "Welcome to Xen";
    const detail = document.createElement("p");
    detail.textContent =
        "Free trial credit is not available for this account. " +
        "You can still use Xen by topping up your balance from £1.";
    copy.append(heading, detail);

    const dismiss = document.createElement("button");
    dismiss.type = "button";
    dismiss.className = "trial-welcome-close";
    dismiss.setAttribute("aria-label", "Dismiss welcome message");
    dismiss.textContent = "×";
    dismiss.addEventListener("click", () => banner.remove());

    localStorage.setItem(seenKey, "1");
    banner.append(copy, dismiss);
    messages.prepend(banner);
}

function isLikelyMobileWorkspace() {
    const hasCoarsePointer = window.matchMedia("(pointer: coarse)").matches;
    const narrowViewport = window.matchMedia("(max-width: 820px)").matches;
    const shortScreenEdge = Math.min(screen.width, screen.height);

    return hasCoarsePointer && (narrowViewport || shortScreenEdge <= 820);
}

function showMobileWorkspaceNotice() {
    if (!isLikelyMobileWorkspace() ||
        localStorage.getItem(mobileWorkspaceNoticeKey) === "1" ||
        document.querySelector(".mobile-workspace-notice")) {
        return;
    }

    const banner = document.createElement("aside");
    banner.className = "trial-welcome mobile-workspace-notice";

    const copy = document.createElement("div");
    const heading = document.createElement("strong");
    heading.textContent = "Desktop recommended";
    const detail = document.createElement("p");
    detail.textContent =
        "Xen works best on a desktop or laptop. Some coding, project and " +
        "Build Plan tools may be difficult to use on a smaller screen.";
    copy.append(heading, detail);

    const dismiss = document.createElement("button");
    dismiss.type = "button";
    dismiss.className = "trial-welcome-close";
    dismiss.setAttribute("aria-label", "Dismiss desktop recommendation");
    dismiss.textContent = "×";
    dismiss.addEventListener("click", () => {
        localStorage.setItem(mobileWorkspaceNoticeKey, "1");
        banner.remove();
    });

    banner.append(copy, dismiss);
    messages.prepend(banner);
}

window.addEventListener("resize", showMobileWorkspaceNotice);

function appendRagDebug(message, debug) {
    const element = document.createElement("div");
    element.className = "rag-debug";
    const matches = Array.isArray(debug.matches)
        ? debug.matches
        : [debug];
    matches.forEach(match => {
        const row = document.createElement("div");
        row.className = match.used ? "rag-debug-row" : "rag-debug-row rejected";
        const confidence = Math.round(
            Math.max(0, Math.min(1, Number(match.similarity) || 0)) * 100);
        row.textContent = match.used
            ? `Knowledge match: ${match.title} · Confidence ${confidence}%`
            : `Knowledge match not used: ${match.title} · Confidence ${confidence}%`;
        element.appendChild(row);
    });
    const content = message.querySelector(".message-content");
    const responseActions = content?.querySelector(".response-code-actions");
    if (content && responseActions)
        content.insertBefore(element, responseActions);
    else
        message.appendChild(element);
}

function addModelFeedbackControls(message, meta) {
    const container = document.createElement("div");
    container.className = "model-feedback-controls";
    container.setAttribute("role", "group");
    container.setAttribute("aria-label", "Rate this response");

    [
        ["up", "Helpful", "👍"],
        ["down", "Not helpful", "👎"]
    ].forEach(([vote, label, icon]) => {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "model-feedback-button";
        button.dataset.vote = vote;
        button.title = label;
        button.setAttribute("aria-label", label);
        button.textContent = icon;
        button.addEventListener("click", async () => {
            container.querySelectorAll("button")
                .forEach(item => item.disabled = true);
            try {
                await sendModelFeedback(meta, vote === "up");
                container.textContent = "Feedback saved";
                container.classList.add("saved");
            } catch (error) {
                container.querySelectorAll("button")
                    .forEach(item => item.disabled = false);
                status.textContent = error.message ||
                    "Your vote could not be saved.";
            }
        });
        container.appendChild(button);
    });

    const content = message.querySelector(".message-content");
    const responseActions = content?.querySelector(".response-code-actions");
    if (responseActions)
        responseActions.prepend(container);
    else if (content)
        content.appendChild(container);
}

async function sendModelFeedback(meta, wasHelpful) {
    const response = await fetch("/api/model-feedback", {
        method: "POST",
        headers: {
            "Authorization": `Bearer ${token}`,
            "Content-Type": "application/json"
        },
        body: JSON.stringify({
            conversationId: meta.conversationId,
            model: meta.model,
            task: meta.task,
            wasHelpful,
            appVersion: document.querySelector(".workspace-build-version")
                ?.textContent.replace(/^Build\s+/i, "").trim() || null,
            userPrompt: meta.userPrompt,
            assistantOutput: buildModelFeedbackOutput(meta, wasHelpful)
        })
    });

    if (response.status === 401) {
        sessionStorage.removeItem("nx_access_token");
        location.replace("/login.html");
        throw new Error("Your session has expired.");
    }
    if (!response.ok) {
        const payload = await response.json().catch(() => ({}));
        throw new Error(payload.detail || payload.message ||
            "Your vote could not be saved.");
    }
}

function buildModelFeedbackOutput(meta, wasHelpful) {
    const output = meta.assistantOutput || "";
    if (wasHelpful || !Array.isArray(meta.ragDebug?.matches))
        return output;

    const matches = meta.ragDebug.matches.filter(match => match.used);
    if (!matches.length)
        return output;

    const lines = matches.map(match => {
        const confidence = Math.round(
            Math.max(0, Math.min(1, Number(match.similarity) || 0)) * 100);
        return `${match.title || "Unknown reference"} · Confidence ${confidence}%`;
    });
    return `${output.trimEnd()}\n\nKnowledge matches:\n${lines.join("\n")}`;
}

function scrollMessagesToBottom() {
    messages.scrollTo({
        top: messages.scrollHeight,
        behavior: "smooth"
    });
}

function renderStructuredResponse(container, source) {
    container.textContent = "";
    if (/^# NinjaTrader (?:Preflight Build|Build Check)\b/im.test(source)) {
        renderPreflightBuildReport(container, source);
        return;
    }

    const fencePattern = /```(?:csharp|cs)?\s*([\s\S]*?)```/gi;
    let cursor = 0;
    let match;
    const generatedCode = [];

    while ((match = fencePattern.exec(source)) !== null) {
        appendProse(container, source.slice(cursor, match.index));
        const code = match[1].trim();
        appendCodeBlock(container, code);
        if (looksLikeCompleteNinjaScript(code))
            generatedCode.push(code);
        cursor = match.index + match[0].length;
    }

    appendProse(container, source.slice(cursor));
    decorateRequirementsMatch(container, source);
    if (generatedCode.length) {
        const primaryCode = generatedCode.reduce(
            (longest, code) =>
                code.length > longest.length ? code : longest,
            "");
        appendResponseCodeActions(container, primaryCode);
    }
}

function isPreflightBuildReport(turn) {
    return turn?.role === "assistant" &&
        /^# NinjaTrader (?:Preflight Build|Build Check)\b/im.test(
            turn.content || "");
}

function compactPreflightBuildHistory(turns) {
    const latestBuildIndex = turns.findLastIndex(isPreflightBuildReport);
    if (latestBuildIndex < 0)
        return turns;

    return turns.filter((turn, index) =>
        !isPreflightBuildReport(turn) || index === latestBuildIndex);
}

function isBacktestReportSource(source) {
    return /^#{1,6}\s+Performance summary\b/im.test(source || "") &&
           /^#{1,6}\s+Key findings\b/im.test(source || "");
}

function renderBacktestReport(container, source) {
    renderStructuredResponse(container, normalizeBacktestReportSource(source));

    const reportHeader = document.createElement("header");
    reportHeader.className = "backtest-report-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("span");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "STRATEGY ANALYZER";
    const title = document.createElement("strong");
    title.textContent = "Backtest performance report";
    const subtitle = document.createElement("small");
    subtitle.textContent =
        "Evidence-based review of the supplied NinjaTrader results";
    heading.append(eyebrow, title, subtitle);

    const savePdf = document.createElement("button");
    savePdf.type = "button";
    savePdf.className = "button backtest-report-pdf";
    savePdf.textContent = "Save report as PDF";
    savePdf.addEventListener(
        "click",
        () => printBacktestReport(container));
    reportHeader.append(heading, savePdf);

    const introduction = document.createElement("p");
    introduction.className = "backtest-report-introduction";
    introduction.textContent = BACKTEST_REPORT_INTRODUCTION;

    const sections = document.createElement("div");
    sections.className = "backtest-report-sections";
    let section = null;
    for (const node of [...container.childNodes]) {
        if (node.nodeType === Node.ELEMENT_NODE &&
            (node.tagName === "H2" || node.tagName === "H3")) {
            section = document.createElement("section");
            section.className = "backtest-report-section";
            sections.appendChild(section);
        }

        if (!section) {
            section = document.createElement("section");
            section.className = "backtest-report-section introduction";
            sections.appendChild(section);
        }
        section.appendChild(node);
    }

    container.replaceChildren(reportHeader, introduction, sections);
}

function normalizeBacktestReportSource(source) {
    return (source || "").replace(
        /\\\[\s*\\frac\{([^{}\r\n]+)\}\{([^{}\r\n]+)\}\s*\\approx\s*([^\\\r\n]+)\s*\\\]/g,
        (_, numerator, denominator, result) =>
            `${numerator.trim()} / (${denominator.trim()}) = approximately ${result.trim()}`);
}

function printBacktestReport(container) {
    const reportSections = container.querySelector(
        ".backtest-report-sections");
    if (!reportSections)
        return;

    const reportWindow = window.open("", "_blank");
    if (!reportWindow) {
        status.textContent =
            "Allow pop-ups to save the backtest report as a PDF";
        return;
    }

    reportWindow.opener = null;
    const reportTitle = currentProjectTitle ||
        "NinjaTrader Strategy Analyzer Report";
    reportWindow.document.write(`<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>${escapeHtml(reportTitle)}</title>
    <style>
        @page { size: A4; margin: 16mm; }
        * { box-sizing: border-box; }
        body { margin: 0; color: #202124; font: 10.5pt/1.55 Arial, sans-serif; }
        header { margin-bottom: 18px; padding-bottom: 14px; border-bottom: 2px solid #f04416; }
        h1 { margin: 0 0 5px; font-size: 20pt; }
        header p { margin: 0; color: #60646b; }
        .report-introduction { margin: 0 0 18px; padding: 10px 12px;
            border-left: 3px solid #f04416; color: #4f5359; background: #f6f7f8; }
        section { break-inside: auto; margin: 0 0 15px; padding: 0; }
        h2, h3 { break-after: avoid-page; margin: 0 0 9px; color: #b52e0c;
            font-size: 13pt; }
        p { break-inside: avoid; margin: 0 0 8px; orphans: 3; widows: 3; }
        ul { margin: 7px 0 0; padding-left: 20px; }
        li { break-inside: avoid; margin: 0 0 6px; orphans: 3; widows: 3; }
        code { padding: 1px 4px; border-radius: 3px; background: #f0f1f3; }
        .notice { margin-top: 15px; color: #666; font-size: 8.5pt; }
    </style>
</head>
<body>
    <header>
        <h1>${escapeHtml(reportTitle)}</h1>
        <p>NinjaTrader Strategy Analyzer review generated by Xen</p>
    </header>
    <p class="report-introduction">${escapeHtml(BACKTEST_REPORT_INTRODUCTION)}</p>
    ${reportSections.innerHTML}
    <p class="notice">Backtest results are historical and do not guarantee future performance.</p>
</body>
</html>`);
    reportWindow.document.close();
    reportWindow.focus();
    window.setTimeout(() => reportWindow.print(), 250);
}

function renderPreflightBuildReport(container, source) {
    const passed = /^## Build passed\b/im.test(source);
    const message = container.closest(".message");
    message?.classList.add(
        "preflight-build-message",
        passed ? "passed" : "failed");

    const heading = document.createElement("div");
    heading.className = "preflight-result-heading";

    const title = document.createElement("div");
    title.className = "preflight-result-title";
    title.textContent = "NinjaTrader Build Check";

    const diagnostics = passed ? [] : parsePreflightDiagnostics(source);
    const groups = passed ? [] : groupPreflightDiagnostics(diagnostics);
    const summary = document.createElement("span");
    summary.className =
        `preflight-result-summary ${passed ? "passed" : "failed"}`;
    summary.textContent = passed
        ? "Build passed"
        : diagnostics.length
            ? `${diagnostics.length} ${diagnostics.length === 1 ? "error" : "errors"} · ` +
              `${groups.length} unique ${groups.length === 1 ? "issue" : "issues"}`
            : "Build failed";
    heading.append(title, summary);
    container.appendChild(heading);

    if (passed) {
        const success = document.createElement("p");
        success.className = "preflight-result-description";
        success.textContent =
            "Xen checked the source against the installed NinjaTrader assemblies and found no build errors.";
        container.appendChild(success);
    } else {
        if (!diagnostics.length) {
            const unavailable = document.createElement("p");
            unavailable.className = "preflight-result-description";
            unavailable.textContent =
                "The build check failed without structured compiler diagnostics.";
            container.appendChild(unavailable);
        }

        const panel = document.createElement("div");
        panel.className = "preflight-diagnostics";
        panel.hidden = groups.length === 0;
        panel.setAttribute("role", "list");
        panel.setAttribute(
            "aria-label",
            `${diagnostics.length} compiler errors`);

        for (const group of groups) {
            const row = document.createElement("div");
            row.className = "preflight-diagnostic";
            row.setAttribute("role", "listitem");

            const metadata = document.createElement("div");
            metadata.className = "preflight-diagnostic-metadata";

            const code = document.createElement("span");
            code.className = "preflight-diagnostic-code";
            code.textContent = group.code;

            const location = document.createElement("span");
            location.className = "preflight-diagnostic-location";
            location.textContent = formatDiagnosticLocations(group.locations);
            metadata.append(code, location);

            const errorMessage = document.createElement("div");
            errorMessage.className = "preflight-diagnostic-message";
            errorMessage.textContent = group.message;
            row.append(metadata, errorMessage);
            panel.appendChild(row);
        }
        container.appendChild(panel);
    }

    const reminder = document.createElement("p");
    reminder.className = "preflight-result-reminder";
    reminder.textContent = passed
        ? "Final compilation and behavioural testing in NinjaTrader are still required."
        : "The source was not executed. Repair these errors, then run the build check again.";
    container.appendChild(reminder);

    if (!passed && diagnostics.length) {
        appendPreflightRepairActions(container, diagnostics);
    }
    normalizePreflightRepairActions();
}

async function downloadNinjaTraderAddon(code, button) {
    if (button.disabled)
        return;

    const originalLabel = button.textContent;
    button.disabled = true;
    button.textContent = "Preparing Add-On...";
    status.textContent = "Creating NinjaTrader Add-On...";

    try {
        const response = await fetch("/api/preflight/addon", {
            method: "POST",
            headers: {
                "Authorization": `Bearer ${token}`,
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                code,
                task: activeTask
            })
        });

        if (response.status === 401) {
            sessionStorage.removeItem("nx_access_token");
            location.replace("/login.html");
            return;
        }

        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(
                error.message ||
                "The NinjaTrader Add-On could not be created.");
        }

        const blob = await response.blob();
        const disposition =
            response.headers.get("Content-Disposition") || "";
        const encodedName = disposition.match(/filename\*=UTF-8''([^;]+)/i);
        const quotedName = disposition.match(/filename="?([^";]+)"?/i);
        const fileName = encodedName
            ? decodeURIComponent(encodedName[1])
            : quotedName?.[1] || "NinjaTrader-Xen-Addon.zip";
        const link = document.createElement("a");
        link.href = URL.createObjectURL(blob);
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(link.href);
        status.textContent = "NinjaTrader Add-On downloaded";
    } catch (error) {
        status.textContent =
            error.message || "The NinjaTrader Add-On download failed.";
    } finally {
        button.disabled = false;
        button.textContent = originalLabel;
    }
}

function appendPreflightRepairActions(container, errors) {
    const actions = document.createElement("div");
    actions.className = "preflight-build-actions";

    const repair = document.createElement("button");
    repair.type = "button";
    repair.className = "button primary";
    repair.textContent = "Repair build errors";
    repair.addEventListener("click", () => {
        if (generating || repair.disabled)
            return;

        repair.disabled = true;
        startPreflightRepair(errors);
    });
    actions.appendChild(repair);

    if (countConsecutivePreflightRepairs() >= 2 &&
        modelSelect.value !== "gpt-5.3-codex") {
        const retryWithCodex = document.createElement("button");
        retryWithCodex.type = "button";
        retryWithCodex.className = "button preflight-codex-retry";
        retryWithCodex.textContent = "Retry repair with Codex 5.3";
        retryWithCodex.addEventListener("click", () => {
            if (generating || retryWithCodex.disabled)
                return;

            repair.disabled = true;
            retryWithCodex.disabled = true;
            startPreflightRepair(errors, "gpt-5.3-codex");
        });
        actions.appendChild(retryWithCodex);
    }

    container.appendChild(actions);
}

function normalizePreflightRepairActions() {
    const reports = [
        ...messages.querySelectorAll(".preflight-build-message")
    ];
    const latest = reports.at(-1);
    for (const report of reports) {
        const keep =
            report === latest && report.classList.contains("failed");
        if (!keep)
            report.querySelectorAll(".preflight-build-actions")
                .forEach(actions => actions.remove());
    }
}

function parsePreflightDiagnostics(source) {
    const diagnostics = [];
    const pattern =
        /^-\s+([A-Z]+\d+|BUILD|TIMEOUT)\s+at\s+(line\s+(\d+)(?:,\s*column\s+(\d+))?|build):\s+(.+)$/gim;
    let match;
    while ((match = pattern.exec(source)) !== null) {
        diagnostics.push({
            code: match[1].toUpperCase(),
            line: match[3] ? Number.parseInt(match[3], 10) : null,
            column: match[4] ? Number.parseInt(match[4], 10) : null,
            message: match[5].trim()
        });
    }
    return diagnostics;
}

function groupPreflightDiagnostics(diagnostics) {
    const groups = new Map();
    for (const diagnostic of diagnostics) {
        const key = `${diagnostic.code}\u0000${diagnostic.message}`;
        if (!groups.has(key)) {
            groups.set(key, {
                code: diagnostic.code,
                message: diagnostic.message,
                locations: []
            });
        }
        groups.get(key).locations.push({
            line: diagnostic.line,
            column: diagnostic.column
        });
    }
    return [...groups.values()];
}

function formatDiagnosticLocations(locations) {
    const formatted = locations.map(location =>
        location.line
            ? `${location.line}${location.column ? `:${location.column}` : ""}`
            : "Build");
    if (formatted.every(location => location === "Build"))
        return "Build";

    const visible = formatted.slice(0, 5);
    return `${formatted.length > 1 ? "Lines" : "Line"} ${visible.join(", ")}`;
}

function decorateRequirementsMatch(container, source) {
    if (!/^# Requirements Verification\b/im.test(source))
        return;

    const match = source.match(
        /##\s+Overall Match[\s\S]*?(?:Approximately\s+)?(\d{1,3})\s*%/i);
    if (!match)
        return;

    const percentage = Math.max(
        0,
        Math.min(100, Number.parseInt(match[1], 10)));
    const heading = [...container.querySelectorAll("h3")]
        .find(element =>
            element.textContent.trim().toLowerCase() === "overall match");
    if (!heading)
        return;

    const badge = document.createElement("span");
    badge.className = "requirements-match-badge";
    if (percentage === 100) {
        badge.classList.add("complete");
        badge.textContent = `${percentage}% · Complete`;
    } else if (percentage >= 70) {
        badge.classList.add("review");
        badge.textContent = `${percentage}% · Review required`;
    } else {
        badge.classList.add("action");
        badge.textContent = `${percentage}% · Action required`;
    }
    heading.appendChild(badge);
}

function renderUserMessage(container, source) {
    container.textContent = "";
    const text = (source || "").replace(/\r/g, "").trim();
    if (!text)
        return;

    if (isAnalyzerRequest(text)) {
        renderAnalyzerUserMessage(container, text);
        return;
    }

    const fencePattern = /```([A-Za-z0-9+#._-]*)[ \t]*\n?([\s\S]*?)```/g;
    let cursor = 0;
    let match;
    let foundFence = false;

    while ((match = fencePattern.exec(text)) !== null) {
        foundFence = true;
        appendUserProse(container, text.slice(cursor, match.index));
        appendCodeBlock(
            container,
            match[2].trim(),
            sourceLanguageLabel(match[1]),
            false);
        cursor = match.index + match[0].length;
    }

    if (foundFence) {
        appendUserProse(container, text.slice(cursor));
        return;
    }

    const uploadedSource = text.match(
        /^([\s\S]*?\bSource file:\s*([^\n]+)\n\n)([\s\S]+)$/i);
    if (uploadedSource) {
        appendUserProse(container, uploadedSource[1].trim());
        appendCodeBlock(
            container,
            uploadedSource[3].trim(),
            sourceLanguageLabel(fileExtension(uploadedSource[2])),
            false);
        return;
    }

    const codeStart = findLikelyCodeStart(text);
    if (codeStart >= 0) {
        appendUserProse(container, text.slice(0, codeStart));
        appendCodeBlock(
            container,
            text.slice(codeStart).trim(),
            "Source code",
            false);
        return;
    }

    container.textContent = formatUserMessage(text);
}

function appendUserProse(container, text) {
    const formatted = formatUserMessage(text);
    if (!formatted)
        return;

    const paragraph = document.createElement("p");
    paragraph.className = "user-message-prose";
    paragraph.textContent = formatted;
    container.appendChild(paragraph);
}

function findLikelyCodeStart(text) {
    const lines = text.split("\n");
    if (lines.length < 4)
        return -1;

    let offset = 0;
    for (const line of lines) {
        const trimmed = line.trim();
        if (/^(using\s+[\w.]+\s*;|namespace\s+[\w.]+|#(?:property|include|region)\b|\/\/|\/\*|\[(?:NinjaScriptProperty|Strategy|Indicator|Robot)\b|(?:public|private|protected|internal)\s+(?:sealed\s+|partial\s+)?class\b)/i.test(trimmed)) {
            const candidate = text.slice(offset);
            const codeSignals = [
                /[{};]/,
                /\b(?:class|void|double|int|bool|string)\b/,
                /\b(?:OnBarUpdate|OnStateChange|OnTick|OnStart|strategy|indicator)\b/i
            ].filter(pattern => pattern.test(candidate)).length;
            if (codeSignals >= 2)
                return offset;
        }
        offset += line.length + 1;
    }

    return -1;
}

function fileExtension(fileName) {
    const match = String(fileName || "").trim().match(/\.([A-Za-z0-9]+)$/);
    return match ? match[1] : "";
}

function sourceLanguageLabel(language) {
    const value = String(language || "").toLowerCase().replace(/^\./, "");
    const labels = {
        cs: "C# source",
        csharp: "C# source",
        mq4: "MQL4 source",
        mq5: "MQL5 source",
        mql4: "MQL4 source",
        mql5: "MQL5 source",
        pine: "Pine Script source",
        csv: "Strategy Analyzer CSV"
    };
    return labels[value] || "Source code";
}

function appendProse(container, text) {
    if (!text.trim())
        return;

    const lines = text.replace(/\r/g, "").split("\n");
    let list = null;

    for (const originalLine of lines) {
        const line = originalLine.trim();
        if (!line) {
            list = null;
            continue;
        }

        const heading = line.match(/^(#{1,6})\s+(.+)$/);
        if (heading) {
            const element = document.createElement(
                heading[1].length === 1 ? "h2" : "h3");
            appendInlineFormatting(element, heading[2]);
            container.appendChild(element);
            list = null;
            continue;
        }

        const bullet = line.match(/^[-*]\s+(.+)$/);
        if (bullet) {
            if (!list) {
                list = document.createElement("ul");
                container.appendChild(list);
            }
            const item = document.createElement("li");
            appendInlineFormatting(item, bullet[1]);
            list.appendChild(item);
            continue;
        }

        const paragraph = document.createElement("p");
        appendInlineFormatting(paragraph, line);
        container.appendChild(paragraph);
        list = null;
    }
}

function appendInlineFormatting(container, text) {
    const pattern =
        /(\*\*[^*\n]+?\*\*|`[^`\n]+?`|<code>[^<\n]*?<\/code>|<strong>[^<\n]*?<\/strong>)/gi;
    let cursor = 0;
    let match;

    while ((match = pattern.exec(text)) !== null) {
        if (match.index > cursor) {
            container.appendChild(
                document.createTextNode(text.slice(cursor, match.index)));
        }

        const token = match[0];
        if (token.startsWith("**")) {
            const strong = document.createElement("strong");
            strong.textContent = token.slice(2, -2);
            container.appendChild(strong);
        } else if (/^<strong>/i.test(token)) {
            const strong = document.createElement("strong");
            strong.textContent = token.slice(8, -9);
            container.appendChild(strong);
        } else {
            const code = document.createElement("code");
            code.className = "inline-code";
            code.textContent = token.startsWith("`")
                ? token.slice(1, -1)
                : token.slice(6, -7);
            container.appendChild(code);
        }

        cursor = match.index + token.length;
    }

    if (cursor < text.length) {
        container.appendChild(
            document.createTextNode(text.slice(cursor)));
    }
}

function appendCodeBlock(
    container,
    code,
    languageLabel = "C# · NinjaScript",
    includeDownload = true) {
    const wrapper = document.createElement("section");
    wrapper.className = "code-block";

    const toolbar = document.createElement("div");
    toolbar.className = "code-toolbar";

    const language = document.createElement("span");
    language.textContent = languageLabel;

    toolbar.appendChild(language);
    if (!includeDownload) {
        const actions = document.createElement("div");
        actions.className = "code-actions";
        actions.appendChild(createCopyCodeButton(code));
        toolbar.appendChild(actions);
    }

    const pre = document.createElement("pre");
    const codeElement = document.createElement("code");
    const isCSharp =
        languageLabel.includes("C#") ||
        languageLabel.includes("NinjaScript");
    if (isCSharp) {
        codeElement.className = "language-csharp";
        codeElement.innerHTML = highlightCSharp(code);
    } else {
        codeElement.textContent = code;
    }
    pre.appendChild(codeElement);

    wrapper.append(toolbar, pre);
    container.appendChild(wrapper);
}

function createCopyCodeButton(code) {
    const copyButton = document.createElement("button");
    copyButton.type = "button";
    copyButton.className = "code-action";
    copyButton.textContent = "Copy";
    copyButton.addEventListener("click", async () => {
        await navigator.clipboard.writeText(code);
        copyButton.textContent = "Copied";
        setTimeout(() => copyButton.textContent = "Copy", 1200);
    });
    return copyButton;
}

function appendResponseCodeActions(container, code) {
    const actions = document.createElement("div");
    actions.className = "code-actions response-code-actions";
    actions.setAttribute("role", "group");
    actions.setAttribute("aria-label", "Generated code actions");

    const downloadButton = document.createElement("button");
    downloadButton.type = "button";
    downloadButton.className = "code-action";
    downloadButton.textContent = "Download .cs";
    downloadButton.addEventListener("click", () => downloadCode(code));

    const addonButton = document.createElement("button");
    addonButton.type = "button";
    addonButton.className = "code-action addon-download-button";
    addonButton.textContent = "Download Add-On";

    const buildButton = document.createElement("button");
    buildButton.type = "button";
    buildButton.className = "code-action preflight-build-button";
    buildButton.textContent = "Build Check";

    const addonNotice = document.createElement("div");
    addonNotice.className = "addon-build-notice";
    addonNotice.setAttribute("role", "alert");
    addonNotice.hidden = true;

    buildButton.addEventListener("click", () => {
        addonNotice.hidden = true;
        addonNotice.textContent = "";
        runPreflightBuild(code, buildButton);
    });
    addonButton.addEventListener("click", async () => {
        addonNotice.hidden = true;
        addonNotice.textContent = "";
        if (hasSuccessfulBuildForCode(code)) {
            await downloadNinjaTraderAddon(code, addonButton);
            return;
        }

        addonButton.disabled = true;
        const result = await runPreflightBuild(code, buildButton);
        addonButton.disabled = false;
        if (result?.success)
            await downloadNinjaTraderAddon(code, addonButton);
    });

    const verifyButton = document.createElement("button");
    verifyButton.type = "button";
    verifyButton.className = "code-action verify-requirements-button";
    verifyButton.textContent = "Verify requirements";
    verifyButton.addEventListener(
        "click",
        () => openRequirementsValidation(code));

    actions.append(
        createCopyCodeButton(code),
        downloadButton,
        addonButton,
        buildButton,
        verifyButton);
    container.append(actions, addonNotice);
}

function hasSuccessfulBuildForCode(code) {
    const expected = code.trim();
    let sourceIndex = -1;

    for (let index = history.length - 1; index >= 0; index--) {
        const turn = history[index];
        if (turn.role !== "assistant")
            continue;
        const blocks = [...(turn.content || "").matchAll(
            /```(?:csharp|cs)?\s*([\s\S]*?)```/gi)];
        if (blocks.some(block => block[1].trim() === expected)) {
            sourceIndex = index;
            break;
        }
    }

    if (sourceIndex < 0)
        return false;

    let passed = null;
    for (let index = sourceIndex + 1; index < history.length; index++) {
        const turn = history[index];
        if (turn.role !== "assistant")
            continue;
        if (/```(?:csharp|cs)?\s*[\s\S]*?```/i.test(turn.content || ""))
            break;
        if (!/^# NinjaTrader (?:Preflight Build|Build Check)\b/im.test(
                turn.content || ""))
            continue;
        passed = /^## Build passed\b/im.test(turn.content || "");
    }

    return passed === true;
}

async function runPreflightBuild(code, button, options = {}) {
    const automatic = options.automatic === true;
    if (preflightBuilding || (generating && !automatic))
        return null;

    preflightBuilding = true;
    if (button) {
        button.classList.remove("needs-build-check");
        button.disabled = true;
        button.classList.add("is-checking");
        button.setAttribute("aria-busy", "true");
        button.textContent = "Checking build...";
    }
    if (automatic)
        setBuildPlanStepStatus("build-checking");
    status.textContent = automatic
        ? "Code returned · running automatic Build Check..."
        : "Running NinjaTrader build check...";

    try {
        const response = await fetch("/api/preflight/build", {
            method: "POST",
            headers: {
                "Authorization": `Bearer ${token}`,
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                code,
                task: activeTask
            })
        });
        const result = await response.json().catch(() => ({}));
        if (response.status === 401) {
            sessionStorage.removeItem("nx_access_token");
            location.replace("/login.html");
            return null;
        }
        if (!response.ok)
            throw new Error(
                result.message ||
                "The NinjaTrader build check could not be started.");

        renderPreflightBuildResult(result);
        if (automatic)
            setBuildPlanStepStatus(result.success ? "compiled" : "build-failed");
        status.textContent = result.success
            ? "Build check passed · saving project..."
            : "Build check found errors · saving diagnostics...";
        const saved = await saveCurrentProject(code);
        status.textContent = result.success
            ? (saved ? "Build check passed and saved" : "Build check passed")
            : (saved ? "Build errors saved" : "Build check found errors");
        return result;
    } catch (error) {
        if (automatic)
            setBuildPlanStepStatus("build-failed");
        status.textContent = error.message;
        return null;
    } finally {
        preflightBuilding = false;
        if (button) {
            button.disabled = false;
            button.classList.remove("is-checking");
            button.removeAttribute("aria-busy");
            button.textContent = "Build check";
        }
    }
}

function renderPreflightBuildResult(result) {
    const errors = Array.isArray(result.errors) ? result.errors : [];
    const report = result.success
        ? "# NinjaTrader Build Check\n\n" +
          "## Build passed\n\n" +
          "Xen checked the source against the installed NinjaTrader assemblies and found no build errors.\n\n" +
          "Final compilation and behavioural testing in NinjaTrader are still required."
        : "# NinjaTrader Build Check\n\n" +
          "## Build failed\n\n" +
          `${formatPreflightErrors(errors)}\n\n` +
          "The source was not executed. Repair these errors, then run the build check again.";

    history = history.filter(turn => !isPreflightBuildReport(turn));
    messages.querySelectorAll(".preflight-build-message")
        .forEach(message => message.remove());
    history.push({ role: "assistant", content: report });
    const message = addMessage("assistant", "");
    message.classList.add(
        "preflight-build-message",
        result.success ? "passed" : "failed");
    const content = message.querySelector(".message-content");

    const reportContent = document.createElement("div");
    renderStructuredResponse(reportContent, report);
    content.appendChild(reportContent);
    normalizePreflightRepairActions();

    scrollMessagesToBottom();
}

function startPreflightRepair(errors, requestedModel = null) {
    if (generating)
        return;

    if (requestedModel &&
        [...modelSelect.options].some(option => option.value === requestedModel)) {
        modelSelect.value = requestedModel;
        acceptedModelSelection = requestedModel;
        rememberSelectedModel();
        updateModelCostBadge();
        updateImageUploadUi();
    }

    promptInput.value =
        `${preflightRepairPromptPrefix} the NinjaTrader build check. ` +
        "Fix the exact compiler errors below, preserve all working behaviour " +
        "and explicit requirements, and return one complete compile-ready C# " +
        "file.\n\n" +
        `Compiler errors:\n${formatPreflightErrors(errors)}`;
    updateClearInputButton();
    status.textContent = requestedModel
        ? "Starting compiler-error repair with Codex 5.3..."
        : "Starting compiler-error repair...";
    form.requestSubmit();
}

function countConsecutivePreflightRepairs() {
    let attempts = 0;
    for (let index = history.length - 1; index >= 0; index--) {
        const turn = history[index];
        if (turn.role === "assistant" &&
            /^# NinjaTrader (?:Preflight Build|Build Check)\s+## Build passed\b/im.test(
                turn.content || "")) {
            break;
        }
        if (turn.role !== "user")
            continue;
        if ((turn.content || "").startsWith(preflightRepairPromptPrefix)) {
            attempts++;
            continue;
        }
        break;
    }
    return attempts;
}

function formatPreflightErrors(errors) {
    if (!errors.length)
        return "- BUILD: The build check failed without a structured compiler error.";

    return errors.map(error => {
        const location = error.line
            ? `line ${error.line}${error.column ? `, column ${error.column}` : ""}`
            : "build";
        return `- ${error.code || "BUILD"} at ${location}: ${error.message}`;
    }).join("\n");
}

function highlightCSharp(code) {
    const keywords = new Set([
        "namespace", "using", "public", "private", "protected", "internal",
        "class", "override", "void", "int", "double", "decimal", "bool",
        "string", "return", "if", "else", "for", "foreach", "while",
        "switch", "case", "break", "new", "null", "true", "false",
        "this", "base", "enum", "static", "readonly"
    ]);
    const apiNames = new Set([
        "State", "Calculate", "CurrentBar", "CurrentBars", "OnStateChange",
        "OnBarUpdate", "SetStopLoss", "SetProfitTarget", "EnterLong",
        "EnterShort", "ExitLong", "ExitShort", "AddPlot", "AddDataSeries",
        "Values"
    ]);
    const tokenPattern =
        /\/\/[^\n]*|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|\b[A-Za-z_][A-Za-z0-9_]*\b/g;

    let html = "";
    let cursor = 0;
    let token;

    while ((token = tokenPattern.exec(code)) !== null) {
        html += escapeHtml(code.slice(cursor, token.index));
        const value = token[0];
        let cssClass = "";

        if (value.startsWith("//"))
            cssClass = "syntax-comment";
        else if (value.startsWith("\"") || value.startsWith("'"))
            cssClass = "syntax-string";
        else if (keywords.has(value))
            cssClass = "syntax-keyword";
        else if (apiNames.has(value))
            cssClass = "syntax-api";

        html += cssClass
            ? `<span class="${cssClass}">${escapeHtml(value)}</span>`
            : escapeHtml(value);
        cursor = token.index + value.length;
    }

    return html + escapeHtml(code.slice(cursor));
}

function downloadCode(code) {
    const classMatch = code.match(/\bclass\s+([A-Za-z_][A-Za-z0-9_]*)/);
    const fileName = `${classMatch ? classMatch[1] : "NinjaScript"}.cs`;
    const url = URL.createObjectURL(new Blob([code], { type: "text/plain;charset=utf-8" }));
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    URL.revokeObjectURL(url);
}

function escapeHtml(value) {
    const span = document.createElement("span");
    span.textContent = value ?? "";
    return span.innerHTML;
}

function formatUserMessage(value) {
    return (value || "")
        .replace(/\r/g, "")
        .replace(/\n[ \t]*\n+/g, "\n")
        .trim();
}

function taskIntro(task) {
    const intros = {
        "build-strategy": "Describe the strategy, including entries, exits, risk and calculation mode.",
        "build-indicator": "Describe the calculation, plots, visual behaviour and configurable inputs.",
        "existing-strategy": "Paste the complete strategy source and explain exactly what should change.",
        "existing-indicator": "Paste the complete indicator source and explain exactly what should change.",
        "convert-strategy": "Paste the complete strategy source from another platform, or upload a source file. Xen will convert it into a NinjaTrader 8 Strategy.",
        "convert-indicator": "Paste the complete indicator source from another platform, or upload a source file. Xen will convert it into a NinjaTrader 8 Indicator.",
        "analyse-backtest": "Upload a NinjaTrader Strategy Analyzer Summary CSV. You can also include a Trades CSV for deeper analysis."
    };
    return intros[task];
}

function detectNinjaScriptSourceType(source) {
    const classDeclaration =
        /\bclass\s+[A-Za-z_][A-Za-z0-9_]*(?:\s*<[^>{}]+>)?\s*:\s*([^{]+)\{/g;
    let match;
    while ((match = classDeclaration.exec(source || "")) !== null) {
        const baseType = match[1].match(
            /\b(?:NinjaTrader\.NinjaScript\.)?(Strategy|Indicator)\b/);
        if (baseType)
            return baseType[1].toLowerCase();
    }
    return null;
}

function redirectMismatchedExistingSource(source, fileName = "") {
    if (activeTask !== "existing-strategy" &&
        activeTask !== "existing-indicator") {
        return false;
    }

    const sourceType = detectNinjaScriptSourceType(source);
    const expectedType = activeTask === "existing-strategy"
        ? "strategy"
        : "indicator";
    if (!sourceType || sourceType === expectedType)
        return false;

    const sourceRequest = source;
    activeTask = sourceType === "strategy"
        ? "existing-strategy"
        : "existing-indicator";
    document.querySelectorAll(".task-button").forEach(item =>
        item.classList.toggle("active", item.dataset.task === activeTask));
    document.getElementById("taskTitle").textContent = taskNames[activeTask];
    promptInput.placeholder = taskPlaceholders[activeTask];
    clearBuildPlan();
    startNewProject(false);
    promptInput.value = sourceRequest;
    updateClearInputButton();

    const typeLabel = sourceType === "strategy" ? "Strategy" : "Indicator";
    const taskLabel = sourceType === "strategy"
        ? "Existing Strategy"
        : "Existing Indicator";
    const sourceLabel = fileName ? `${fileName} is` : "This source is";
    const message =
        `${sourceLabel} a NinjaTrader ${typeLabel}. ` +
        `Xen moved it to ${taskLabel} so the correct specialist prompt is used. ` +
        "Review it, then press Send.";
    sourceFileStatus.textContent = message;
    status.textContent = message;
    promptInput.focus();
    promptInput.setSelectionRange(0, 0);
    return true;
}

function startNewProject(resetTask = true) {
    sessionStorage.removeItem(activeProjectStorageKey);
    currentProjectId = null;
    currentProjectTitle = "";
    currentProjectPersisted = false;
    hasProjectSnapshots = false;
    updateHistoryButton();
    history = [];
    existingCodeState = { sources: [], decisions: [], workingCode: null };
    renderExistingCodeAttachments();
    promptInput.value = "";
    clearAnalyzerExports(false);
    updateClearInputButton();

    if (resetTask) {
        activeTask = "build-strategy";
        document.querySelectorAll(".task-button").forEach(item =>
            item.classList.toggle("active", item.dataset.task === activeTask));
        document.getElementById("taskTitle").textContent = taskNames[activeTask];
        promptInput.placeholder = taskPlaceholders[activeTask];
    }

    updateTaskSpecificUi();
    messages.innerHTML = "";
    addMessage("assistant", taskIntro(activeTask));
    updateProjectTitle();
    renderActiveBuildPlan();
    status.textContent = "";
    promptInput.focus();
}

function updateClearInputButton() {
    clearInputButton.hidden =
        generating ||
        promptInput.disabled ||
        (!promptInput.value.trim() &&
            !pendingImage &&
            !pendingAnalyzerExports.length);
}

function clearComposerInput() {
    if (generating)
        return;

    promptInput.value = "";
    clearAnalyzerExports(false);
    updateTaskSpecificUi();
    updateClearInputButton();
    status.textContent = "";
    promptInput.focus();
}

function updateTaskSpecificUi(preservePendingImage = false) {
    const uploadTasks = {
        "convert-strategy": {
            button: "Upload source file",
            status: "One source file · add conversion instructions below",
            accept: ".cs,.txt,.mq4,.mq5,.pine,text/plain"
        },
        "convert-indicator": {
            button: "Upload source file",
            status: "One source file · add conversion instructions below",
            accept: ".cs,.txt,.mq4,.mq5,.pine,text/plain"
        },
        "existing-strategy": {
            button: "Add source code",
            status: "",
            accept: ".cs,.txt,text/plain"
        },
        "existing-indicator": {
            button: "Add source code",
            status: "",
            accept: ".cs,.txt,text/plain"
        },
        "analyse-backtest": {
            button: "Upload Summary CSV (required)",
            status: "Add the Summary, then optionally add Trades for deeper analysis",
            accept: ".csv,text/csv"
        }
    };
    const options = uploadTasks[activeTask];
    sourceImport.hidden = !options;
    sourceFileInput.value = "";
    sourceFileInput.accept = options?.accept || "";
    sourceFileInput.multiple = false;
    analyzerTradesFileInput.value = "";
    analyzerTradesFileButton.hidden = activeTask !== "analyse-backtest";
    sourceFileButton.textContent = options?.button || "Upload source file";
    sourceFileStatus.textContent = options?.status || "";
    renderExistingCodeAttachments();
    analyzerExportGuide.hidden = activeTask !== "analyse-backtest";
    sendButton.querySelector(".send-label").textContent =
        activeTask === "analyse-backtest" ? "Analyse results" : "Send";
    renderAnalyzerAttachments();
    if (!preservePendingImage) {
        pendingImage = null;
        imageFileInput.value = "";
        imagePreview.hidden = true;
        imagePreviewContent.removeAttribute("src");
    }
    updateImageUploadUi();
}

function updateImageUploadUi(message = "") {
    const allowed =
        activeTask === "build-indicator" ||
        activeTask === "convert-indicator";
    imageImport.hidden = !allowed;
    if (!allowed)
        return;

    const supported = !imageUnsupportedModels.has(modelSelect.value);
    imageFileButton.disabled = !supported || generating;
    if (message) {
        imageFileStatus.textContent = message;
    } else if (pendingImage) {
        imageFileStatus.textContent = "";
    } else if (!supported) {
        imageFileStatus.textContent =
            "The selected model does not support images. Choose Sol or Claude.";
    } else {
        imageFileStatus.textContent = "";
    }
}

function handleModelChange() {
    if (pendingImage && imageUnsupportedModels.has(modelSelect.value)) {
        modelSelect.value = acceptedModelSelection;
        updateImageUploadUi(
            "Remove the attached image before selecting a model without image support.");
        updateModelCostBadge();
        return;
    }

    acceptedModelSelection = modelSelect.value;
    rememberSelectedModel();
    updateModelCostBadge();
    updateImageUploadUi();
}

async function importReferenceImage() {
    const file = imageFileInput.files?.[0];
    if (!file)
        return;

    imageFileInput.value = "";
    if (imageUnsupportedModels.has(modelSelect.value)) {
        updateImageUploadUi(
            "The selected model does not support image uploads.");
        return;
    }

    const allowedTypes = ["image/png", "image/jpeg", "image/webp"];
    if (!allowedTypes.includes(file.type)) {
        updateImageUploadUi("Use a PNG, JPEG or WebP reference image.");
        return;
    }

    if (file.size > 3 * 1024 * 1024) {
        updateImageUploadUi("The reference image must be 3 MB or smaller.");
        return;
    }

    try {
        const data = await readFileAsDataUrl(file);
        setPendingImage({
            name: file.name,
            type: file.type,
            data
        });
        promptInput.focus();
    } catch {
        updateImageUploadUi("Xen could not read this reference image.");
    }
}

function readFileAsDataUrl(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.addEventListener("load", () => resolve(reader.result));
        reader.addEventListener("error", reject);
        reader.readAsDataURL(file);
    });
}

function setPendingImage(image) {
    pendingImage = image;
    imagePreviewContent.src = image.data;
    imagePreview.hidden = false;
    updateImageUploadUi();
    updateClearInputButton();
}

function clearPendingImage() {
    pendingImage = null;
    imageFileInput.value = "";
    imagePreviewContent.removeAttribute("src");
    imagePreview.hidden = true;
    updateImageUploadUi();
    updateClearInputButton();
}

function appendSubmittedImage(message, image) {
    const preview = document.createElement("div");
    preview.className = "submitted-image";
    const element = document.createElement("img");
    element.src = image.data;
    element.alt = `Reference image: ${image.name}`;
    preview.appendChild(element);
    message.querySelector(".message-content")?.prepend(preview);
}

async function importSourceFile() {
    const selectedFiles = [...(sourceFileInput.files || [])];
    if (activeTask === "analyse-backtest") {
        await importAnalyzerCsvFile(
            selectedFiles[0],
            "summary",
            sourceFileInput);
        return;
    }

    const file = selectedFiles[0];
    if (!file)
        return;

    const isStrategyConversion = activeTask === "convert-strategy";
    const isIndicatorConversion = activeTask === "convert-indicator";
    const isConversion = isStrategyConversion || isIndicatorConversion;
    const allowedExtensions = isConversion
        ? [".cs", ".txt", ".mq4", ".mq5", ".pine"]
        : [".cs", ".txt"];
    const extension = file.name.includes(".")
        ? file.name.slice(file.name.lastIndexOf(".")).toLowerCase()
        : "";

    if (!allowedExtensions.includes(extension)) {
        sourceFileStatus.textContent = isConversion
            ? "Use a .cs, .txt, .mq4, .mq5 or .pine source file."
            : "Use a NinjaScript .cs or plain-text .txt file.";
        sourceFileInput.value = "";
        return;
    }

    if (file.size > 512 * 1024) {
        sourceFileStatus.textContent =
            "This file is too large. The maximum size is 512 KB.";
        sourceFileInput.value = "";
        return;
    }

    try {
        const source = (await file.text()).trim();
        if (!source) {
            sourceFileStatus.textContent = "The selected file is empty.";
            return;
        }

        if (isExistingCodeTask()) {
            existingCodeFileName.value = file.name;
            existingCodeText.value = source;
            existingCodeRole.value = hasCurrentExistingSource()
                ? "additional-source" : "current-source";
            openExistingCodeModal();
            return;
        }
        if (isConversion) {
            await saveConversionSourceAttachment(file.name, source);
            return;
        }

    } catch {
        sourceFileStatus.textContent = "Xen could not read this source file.";
    } finally {
        sourceFileInput.value = "";
    }
}

async function importAnalyzerTradesFile() {
    const file = analyzerTradesFileInput.files?.[0];
    await importAnalyzerCsvFile(file, "trades", analyzerTradesFileInput);
}

function isExistingCodeTask(task = activeTask) {
    return task === "existing-strategy" || task === "existing-indicator";
}

function isSourceAttachmentTask(task = activeTask) {
    return isExistingCodeTask(task) ||
        task === "convert-strategy" || task === "convert-indicator";
}

function isNewCodeBuildTask(task = activeTask) {
    return task === "build-strategy" || task === "build-indicator";
}

function hasCurrentExistingSource() {
    return existingCodeState.sources.some(source => source.role === "current-source");
}

function looksLikeCompleteNinjaScript(value) {
    return /\bclass\s+[A-Za-z_][A-Za-z0-9_]*[\s\S]{0,300}:\s*(?:[\w.]+\.)?(?:Strategy|Indicator)\b/.test(value) &&
        /\bOnStateChange\s*\(/.test(value);
}

function openExistingCodeModal() {
    if (!isExistingCodeTask()) return;
    existingCodeStatus.textContent = "";
    if (!existingCodeFileName.value)
        existingCodeFileName.value = activeTask === "existing-strategy"
            ? "Strategy.cs" : "Indicator.cs";
    const hasCurrent = hasCurrentExistingSource();
    document.getElementById("existingCodeCurrentRole").textContent =
        hasCurrent ? "Replace current source" : "Current codebase";
    existingCodeRole.value = hasCurrent
        ? "additional-source" : "current-source";
    existingCodeRole.disabled = !hasCurrent;
    existingCodeRole.title = hasCurrent
        ? "Choose how this source should be used"
        : "The first source is always saved as the Current codebase";
    existingCodeModal.hidden = false;
    document.body.classList.add("modal-open");
    window.setTimeout(() => existingCodeText.focus(), 0);
}

function closeExistingCodeModal() {
    existingCodeModal.hidden = true;
    document.body.classList.remove("modal-open");
    existingCodeStatus.textContent = "";
}

async function saveConversionSourceAttachment(fileName, code) {
    if (!currentProjectId) {
        currentProjectId = crypto.randomUUID();
        currentProjectTitle = taskNames[activeTask];
        updateProjectTitle();
    }
    const source = {
        id: crypto.randomUUID().replaceAll("-", ""),
        fileName,
        role: "current-source",
        code
    };
    sourceFileStatus.textContent = "Saving source attachment...";
    const response = await fetch(`/api/projects/${currentProjectId}/existing-code`, {
        method: "PUT",
        headers: {
            "Authorization": `Bearer ${token}`,
            "Content-Type": "application/json"
        },
        body: JSON.stringify({ task: activeTask, sources: [source] })
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
        sourceFileStatus.textContent = payload.message || "Source could not be saved.";
        return;
    }
    existingCodeState = payload;
    renderExistingCodeAttachments();
    sourceFileStatus.textContent = "Source attached · add conversion instructions below";
    promptInput.focus();
}

async function saveExistingCodeAttachment(event) {
    event.preventDefault();
    const code = existingCodeText.value.trim();
    const fileName = existingCodeFileName.value.trim();
    if (!code || !fileName) return;
    if (!currentProjectId) {
        currentProjectId = crypto.randomUUID();
        currentProjectTitle = taskNames[activeTask];
        updateProjectTitle();
    }
    const hasCurrent = hasCurrentExistingSource();
    const source = {
        id: crypto.randomUUID().replaceAll("-", ""),
        fileName,
        role: hasCurrent ? existingCodeRole.value : "current-source",
        code
    };
    const sources = [...existingCodeState.sources];
    if (source.role !== "current-source" && sources.length >= 4) {
        existingCodeStatus.textContent =
            "A project can contain no more than four source files. Remove one before adding another.";
        return;
    }
    if (source.role === "current-source") {
        const index = sources.findIndex(item => item.role === "current-source");
        if (index >= 0) sources[index] = source;
        else sources.unshift(source);
    } else {
        sources.push(source);
    }
    existingCodeStatus.textContent = "Saving source with this project...";
    const response = await fetch(`/api/projects/${currentProjectId}/existing-code`, {
        method: "PUT",
        headers: {
            "Authorization": `Bearer ${token}`,
            "Content-Type": "application/json"
        },
        body: JSON.stringify({ task: activeTask, sources })
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
        existingCodeStatus.textContent = payload.message || "Source could not be saved.";
        return;
    }
    existingCodeState = payload;
    promptInput.value = stripCompleteSourceFromPrompt(promptInput.value);
    existingCodeText.value = "";
    renderExistingCodeAttachments();
    closeExistingCodeModal();
    status.textContent = sources.length === 1
        ? ""
        : `${sources.length} source files retained`;
    promptInput.focus();
}

function stripCompleteSourceFromPrompt(value) {
    return looksLikeCompleteNinjaScript(value) ? "" : value;
}

function renderExistingCodeAttachments() {
    existingCodeAttachments.replaceChildren();
    const visible = isSourceAttachmentTask() && existingCodeState.sources.length > 0;
    existingCodeAttachments.hidden = !visible;
    if (!visible) return;
    for (const source of existingCodeState.sources) {
        const item = document.createElement("span");
        item.className = "existing-code-attachment";
        const roleLabel = source.role === "additional-source"
            ? "additional source"
            : source.role === "reference-source"
                ? "reference source"
                : "current source";
        const label = document.createElement("span");
        label.textContent = isExistingCodeTask()
            ? `${source.fileName} · ${roleLabel}`
            : source.fileName;
        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "existing-code-attachment-remove";
        remove.setAttribute("aria-label", `Remove ${source.fileName}`);
        remove.title = `Remove ${source.fileName}`;
        remove.textContent = "×";
        remove.disabled = generating;
        remove.addEventListener("click", () => removeExistingCodeAttachment(source.id));
        item.append(label, remove);
        existingCodeAttachments.appendChild(item);
    }
    if (!hasCurrentExistingSource() || !isExistingCodeTask()) return;
    const review = document.createElement("button");
    review.type = "button";
    review.className = "button existing-code-review-button";
    review.textContent = activeTask === "existing-strategy"
        ? "Review strategy" : "Review indicator";
    review.disabled = generating;
    review.addEventListener("click", submitExistingCodeReview);
    existingCodeAttachments.appendChild(review);
}

async function removeExistingCodeAttachment(sourceId) {
    if (generating || !currentProjectId) return;
    const source = existingCodeState.sources.find(item => item.id === sourceId);
    if (source?.role === "current-source" && existingCodeState.sources.some(
        item => item.id !== sourceId && item.role !== "current-source")) {
        status.textContent =
            "Remove Merge and Example sources before removing the Current codebase.";
        return;
    }
    const sources = existingCodeState.sources.filter(source => source.id !== sourceId);
    status.textContent = "Removing source...";
    const response = await fetch(`/api/projects/${currentProjectId}/existing-code`, {
        method: "PUT",
        headers: {
            "Authorization": `Bearer ${token}`,
            "Content-Type": "application/json"
        },
        body: JSON.stringify({ task: activeTask, sources })
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
        status.textContent = payload.message || "Source could not be removed";
        return;
    }
    existingCodeState = payload;
    renderExistingCodeAttachments();
    status.textContent = sources.length
        ? (sources.length === 1 ? "" : `${sources.length} source files retained`)
        : isExistingCodeTask()
            ? "Source removed · add current source before sending requirements"
            : "Source removed · upload one source file to convert";
}

function submitExistingCodeReview(event) {
    if (generating || preparingRequest || !isExistingCodeTask() ||
        !hasCurrentExistingSource())
        return;
    event.currentTarget.disabled = true;
    const scriptType = activeTask === "existing-strategy"
        ? "strategy" : "indicator";
    promptInput.value =
        `Review all attached NinjaScript source files for this ${scriptType}. ` +
        "Explain the current behaviour, identify defects or risks, and list " +
        "any requirements or decisions that need clarification. Do not modify " +
        "or generate code yet.";
    updateClearInputButton();
    form.requestSubmit();
}

async function loadExistingCodeState() {
    existingCodeState = { sources: [], decisions: [], workingCode: null };
    if (!isSourceAttachmentTask() || !currentProjectId) {
        renderExistingCodeAttachments();
        return;
    }
    const response = await fetch(`/api/projects/${currentProjectId}/existing-code`, {
        headers: { "Authorization": `Bearer ${token}` }
    });
    if (response.ok) existingCodeState = await response.json();
    renderExistingCodeAttachments();
}

async function importAnalyzerCsvFile(file, expectedType, input) {
    if (!file)
        return;

    if (!file.name.toLowerCase().endsWith(".csv")) {
        sourceFileStatus.textContent =
            "Use CSV exports from NinjaTrader Strategy Analyzer.";
        input.value = "";
        return;
    }

    if (file.size > 256 * 1024) {
        sourceFileStatus.textContent =
            "Each Analyzer CSV must be 256 KB or smaller.";
        input.value = "";
        return;
    }

    try {
        const csv = (await file.text())
            .replace(/^\uFEFF/, "")
            .replace(/\u0000/g, "")
            .trim();
        const lines = csv.split(/\r?\n/).filter(line => line.trim());
        if (lines.length < 2 ||
            !lines.slice(0, 5).some(line => /[,;\t]/.test(line))) {
            throw new Error(
                `${file.name} does not appear to be a valid Analyzer CSV.`);
        }

        const actualType = /^"?Performance"?[,;\t]/i.test(lines[0])
            ? "summary"
            : /^"?Trade number"?[,;\t]/i.test(lines[0])
                ? "trades"
                : null;
        if (actualType !== expectedType) {
            const expectedLabel = expectedType === "summary" ? "Summary" : "Trades";
            const actualLabel = actualType === "summary" ? "Summary" : "Trades";
            sourceFileStatus.textContent = actualType
                ? `Use the ${actualLabel} upload button for this file.`
                : `${file.name} is not a NinjaTrader ${expectedLabel} CSV.`;
            return;
        }

        const nextExports = pendingAnalyzerExports
            .filter(item => item.type !== expectedType);
        nextExports.push({
            name: file.name,
            csv: csv.replace(/```/g, "'''"),
            type: expectedType,
            summary: expectedType === "summary"
        });
        nextExports.sort((left, right) =>
            left.type === "summary" ? -1 : right.type === "summary" ? 1 : 0);

        const request = buildAnalyzerRequest("", nextExports);
        if (request.length > promptInput.maxLength) {
            sourceFileStatus.textContent =
                "The selected exports are too large for one analysis. Upload the Summary alone or a smaller Trades export.";
            return;
        }

        pendingAnalyzerExports = nextExports;
        renderAnalyzerAttachments();
        if (!promptInput.value.trim()) {
            promptInput.value = [
                "Instrument: ",
                "Bar type and timeframe: ",
                "Test period and notes: "
            ].join("\n");
        }
        updateClearInputButton();
        sourceFileStatus.textContent =
            pendingAnalyzerExports.some(item => item.summary)
                ? `${pendingAnalyzerExports.length} file${pendingAnalyzerExports.length === 1 ? "" : "s"} ready - add context, then analyse`
                : "Trades ready - add the required Summary CSV";
        promptInput.focus();
        const firstValuePosition = promptInput.value.indexOf(":") + 2;
        promptInput.setSelectionRange(firstValuePosition, firstValuePosition);
    } catch (error) {
        sourceFileStatus.textContent =
            error.message || "Xen could not read the Analyzer export.";
    } finally {
        input.value = "";
    }
}

function buildAnalyzerRequest(context, exports) {
    return [
        "Analyse the following NinjaTrader Strategy Analyzer export.",
        "",
        "Test context:",
        context.trim() || "Not provided.",
        "",
        ...exports.flatMap(item => [
            `Export file: ${item.name}`,
            "```csv",
            item.csv,
            "```",
            ""
        ])
    ].join("\n").trim();
}

function isAnalyzerRequest(value) {
    return /^Analyse the following NinjaTrader Strategy Analyzer export\./i
        .test((value || "").trim());
}

function analyzerContextFromRequest(value) {
    const match = (value || "").match(
        /Test context:\s*\n([\s\S]*?)(?=\n\s*Export file:)/i);
    const context = match?.[1]?.trim() || "";
    return context === "Not provided." ? "" : context;
}

function analyzerFileNamesFromRequest(value) {
    return [...(value || "").matchAll(/^Export file:\s*(.+)$/gim)]
        .map(match => match[1].trim())
        .filter(Boolean);
}

function renderAnalyzerUserMessage(container, value) {
    const files = analyzerFileNamesFromRequest(value);
    const context = analyzerContextFromRequest(value);
    const heading = document.createElement("p");
    heading.className = "user-message-prose";
    heading.textContent = "Analyse Strategy Analyzer results";
    container.appendChild(heading);

    if (files.length) {
        const attachments = document.createElement("div");
        attachments.className = "submitted-analyzer-files";
        files.forEach(name => {
            const item = document.createElement("span");
            item.textContent = name;
            attachments.appendChild(item);
        });
        container.appendChild(attachments);
    }

    if (context)
        appendUserProse(container, context);
}

function hasAnalyzerExportInHistory() {
    return history.some(turn =>
        turn.role === "user" && isAnalyzerRequest(turn.content));
}

function renderAnalyzerAttachments() {
    analyzerAttachments.replaceChildren();
    const visible =
        activeTask === "analyse-backtest" &&
        pendingAnalyzerExports.length > 0;
    analyzerAttachments.hidden = !visible;
    if (!visible)
        return;

    pendingAnalyzerExports.forEach((item, index) => {
        const chip = document.createElement("span");
        chip.className = "analyzer-attachment";
        const name = document.createElement("span");
        const label = item.summary ? "Summary" : "Trades";
        name.textContent = `${label}: ${item.name}`;
        const remove = document.createElement("button");
        remove.type = "button";
        remove.setAttribute("aria-label", `Remove ${item.name}`);
        remove.textContent = "\u00d7";
        remove.addEventListener("click", () => {
            pendingAnalyzerExports.splice(index, 1);
            renderAnalyzerAttachments();
            const hasSummary = pendingAnalyzerExports.some(entry => entry.summary);
            sourceFileStatus.textContent = hasSummary
                ? `${pendingAnalyzerExports.length} file${
                    pendingAnalyzerExports.length === 1 ? "" : "s"} ready`
                : pendingAnalyzerExports.length
                    ? "Trades ready - add the required Summary CSV"
                    : "Summary CSV required - Trades CSV optional";
            updateClearInputButton();
        });
        chip.append(name, remove);
        analyzerAttachments.appendChild(chip);
    });
}

function clearAnalyzerExports(updateStatus = true) {
    pendingAnalyzerExports = [];
    renderAnalyzerAttachments();
    if (updateStatus && activeTask === "analyse-backtest")
        sourceFileStatus.textContent =
            "Summary CSV required - Trades CSV optional";
}

let resolvePromptBuilder = null;

async function reviewBuildPrompt(prompt) {
    if (isSavedBuildPlanPrompt(prompt) ||
        prompt.trimStart().startsWith(
            "Repair the latest complete NinjaScript source")) {
        promptReviewCompleted = true;
        return prompt;
    }

    if (!["build-strategy", "build-indicator"].includes(activeTask)) {
        return prompt;
    }

    // Prompt Builder defines a new build. Once a project has started, all
    // later requests modify the authoritative implementation directly.
    if (looksLikeCompleteNinjaScript(getLatestGeneratedCode())) {
        promptReviewCompleted = true;
        return prompt;
    }

    setComposerReviewState(true, "Reviewing your request...");
    try {
        const planningPrompt = buildPromptBuilderContext(prompt);
        const response = await promptBuilderFetch("/api/prompt-builder/check", {
            task: activeTask,
            prompt: planningPrompt,
            hasCurrentCode: Boolean(getLatestGeneratedCode()),
            previousAssistantResponse: getLatestHistoryContent("assistant")
        });

        if (response.stopMessage) {
            addMessage("user", prompt);
            const notice = addMessage("assistant", response.stopMessage);
            promptInput.value = "";
            updateClearInputButton();
            const targetTask = response.targetTask ||
                (activeTask === "build-indicator"
                    ? "build-strategy"
                    : "build-indicator");
            appendTaskSwitchAction(notice, targetTask, prompt);
            scrollMessagesToBottom();
            return null;
        }

        if (!response.recommendPromptBuilder) {
            promptReviewCompleted = true;
            return prompt;
        }

        const decision = await showPromptReview(
            response.reason,
            response.required === true);
        if (decision === "build") {
            promptReviewCompleted = true;
            promptBuilderBypassed = true;
            return prompt;
        }
        if (decision !== "clarify")
            return null;

        const improvedPrompt = await runPromptBuilder(prompt);
        if (!improvedPrompt)
            return null;

        promptInput.value = improvedPrompt;
        updateClearInputButton();
        promptReviewCompleted = true;
        return improvedPrompt;
    } catch {
        return prompt;
    } finally {
        setComposerReviewState(false, "");
    }
}

function showPromptReview(reason, required = false) {
    promptBuilderModal.querySelector(".prompt-builder-dialog")
        ?.classList.remove("prompt-builder-plan-dialog");
    document.getElementById("promptBuilderTitle").textContent =
        "Plan this build first?";
    promptBuilderReason.textContent = reason ||
        "A few details could materially improve the generated NinjaScript.";
    promptBuilderBody.replaceChildren(createReviewSummary());
    promptBuilderStatus.textContent = "";
    const actions = [
        ["Plan with Prompt Builder", "clarify", "button primary"],
        ["Edit original request", "edit", "button"]
    ];
    if (!required)
        actions.splice(1, 0,
            [pendingImage ? "Build directly from image" : "Build in Xen anyway",
                "build", "button"]);
    setPromptBuilderActions(actions);
    openPromptBuilder();
    return waitForPromptBuilderDecision();
}

async function runPromptBuilder(prompt) {
    const planningPrompt = buildPromptBuilderContext(prompt);
    document.getElementById("promptBuilderTitle").textContent =
        "Clarify your build request";
    showPromptBuilderWait("Preparing clarification questions...");

    try {
        const result = await promptBuilderFetch("/api/prompt-builder/questions", {
            task: activeTask,
            prompt: planningPrompt
        });
        renderPromptQuestions(result.questions);
        promptBuilderReason.textContent = pendingImage
            ? "Your reference image will stay attached. Answer every question for more control, or build directly from the image and let Xen infer the indicator."
            : "Answer every clarification question before creating the Build Plan, or let Xen suggest editable baseline answers.";
        promptBuilderStatus.textContent = "";
        setPromptBuilderActions([
            ["Create Build Plan", "compose", "button primary"],
            [pendingImage ? "Build directly from image" : "Build original request",
                "build", "button"],
            ["Edit request", "edit", "button"]
        ]);
        updatePromptBuilderComposeState();
        addBaselineSuggestionAction(prompt);

        const decision = await waitForPromptBuilderDecision();
        if (decision === "build") {
            promptReviewCompleted = true;
            promptBuilderBypassed = true;
            return prompt;
        }
        if (decision !== "compose")
            return null;

        const answers = [...promptBuilderBody.querySelectorAll("[data-question]")]
            .map(field => ({
                question: field.dataset.question,
                answer: field.value.trim()
            }));

        showPromptBuilderWait("Creating your Build Plan. This can take up to a minute...");
        const planResult = await promptBuilderFetch("/api/prompt-builder/compose", {
            task: activeTask,
            prompt: planningPrompt,
            answers
        });
        ensurePlanUsesReferenceImage(planResult);
        const plan = saveBuildPlan(planResult, activeTask, prompt);
        renderActiveBuildPlan();
        const action = await showBuildPlan(plan);
        if (action === "start")
            loadBuildPlanPrompt(plan.currentIndex);
        return null;
    } catch (error) {
        openPromptBuilder();
        promptBuilderBody.classList.remove("waiting");
        promptBuilderBody.replaceChildren();
        promptBuilderReason.textContent =
            "The Build Plan was not created. You can retry by editing the request, or continue with the original request you entered before the questions.";
        promptBuilderStatus.textContent =
            error.message || "Prompt Builder is temporarily unavailable.";
        promptBuilderStatus.classList.add("error");
        setPromptBuilderActions([
            [pendingImage ? "Build directly from image" : "Build original request",
                "build", "button primary"],
            ["Edit request", "edit", "button"]
        ]);
        const fallback = await waitForPromptBuilderDecision();
        return fallback === "build" ? prompt : null;
    } finally {
        setPromptBuilderBusy(false);
    }
}

function addBaselineSuggestionAction(prompt) {
    const row = document.createElement("div");
    row.className = "prompt-builder-suggestion-link-row";
    const button = document.createElement("button");
    button.type = "button";
    button.className = "prompt-builder-suggestion-link";
    button.textContent = "Let Xen suggest baseline answers";
    button.addEventListener("click", async () => {
        const fields = [...promptBuilderBody.querySelectorAll("[data-question]")]
            .filter(field => !field.value.trim());
        if (!fields.length) {
            promptBuilderStatus.textContent =
                "All clarification questions already have answers.";
            return;
        }

        setPromptBuilderBusy(true);
        setPromptBuilderLoading("Xen is suggesting editable baseline answers...");
        try {
            const result = await promptBuilderFetch(
                "/api/prompt-builder/suggestions", {
                    task: activeTask,
                    prompt: buildPromptBuilderContext(prompt),
                    questions: fields.map(field => field.dataset.question)
                });
            const answers = Array.isArray(result.answers) ? result.answers : [];
            if (answers.length !== fields.length)
                throw new Error("Xen did not return every baseline suggestion.");

            fields.forEach((field, index) => {
                if (field.value.trim())
                    return;
                field.value = answers[index];
                const container = field.closest(".prompt-builder-field");
                container?.classList.add("suggested");
                if (container &&
                    !container.querySelector(".prompt-builder-suggestion-note")) {
                    const note = document.createElement("small");
                    note.className = "prompt-builder-suggestion-note";
                    note.textContent =
                        "Xen suggestion - review and edit before creating the plan.";
                    container.appendChild(note);
                }
            });
            updatePromptBuilderComposeState();
            promptBuilderStatus.textContent =
                "Editable baseline suggestions added to blank answers.";
            promptBuilderStatus.classList.remove("error");
        } catch (error) {
            promptBuilderStatus.textContent =
                error.message || "Xen could not suggest baseline answers.";
            promptBuilderStatus.classList.add("error");
        } finally {
            setPromptBuilderBusy(false);
        }
    });
    row.appendChild(button);
    promptBuilderBody.prepend(row);
}

function buildPromptBuilderContext(prompt) {
    if (!pendingImage)
        return prompt;

    return `${prompt}\n\nReference image context: A reference image is attached to this build request. The coding model must inspect it and use it as the visual specification for the indicator. Prompt Builder should ask only for requirements that cannot be determined safely from the image.`;
}

function ensurePlanUsesReferenceImage(planResult) {
    if (!pendingImage || !Array.isArray(planResult?.prompts) ||
        !planResult.prompts.length)
        return;

    const firstPrompt = planResult.prompts[0];
    if (!firstPrompt?.prompt ||
        /reference image|attached image/i.test(firstPrompt.prompt))
        return;

    firstPrompt.prompt =
        `Inspect and use the attached reference image as the visual specification.\n\n${firstPrompt.prompt}`;
}

function saveBuildPlan(result, task, originalPrompt) {
    const plan = {
        version: 2,
        projectId: currentProjectId,
        task,
        originalPrompt: originalPrompt?.trim() || "",
        explanation: result.explanation || "",
        assumptions: Array.isArray(result.assumptions) ? result.assumptions : [],
        prompts: Array.isArray(result.prompts) ? result.prompts : [],
        currentIndex: 0,
        stepStatus: "ready",
        completed: false,
        createdAt: new Date().toISOString()
    };
    localStorage.setItem(buildPlanStorageKey, JSON.stringify(plan));
    return plan;
}

function getBuildPlan() {
    try {
        const plan = JSON.parse(localStorage.getItem(buildPlanStorageKey));
        if (plan?.version === 1 && plan.stepStatus === "sent") {
            plan.version = 2;
            plan.stepStatus = "response-ready";
            localStorage.setItem(buildPlanStorageKey, JSON.stringify(plan));
        }
        if (!plan || !Array.isArray(plan.prompts) || !plan.prompts.length)
            return null;
        if (plan.projectId && plan.projectId !== currentProjectId)
            return null;
        return plan;
    } catch {
        return null;
    }
}

function updateBuildPlan(plan) {
    localStorage.setItem(buildPlanStorageKey, JSON.stringify(plan));
    renderActiveBuildPlan();
}

function clearBuildPlan() {
    localStorage.removeItem(buildPlanStorageKey);
    renderActiveBuildPlan();
}

function exitBuildPlan() {
    const plan = getBuildPlan();
    if (!plan || !window.confirm(
        "Exit this Build Plan? Completed work and conversation history will remain saved.")) {
        return;
    }

    const index = Math.min(
        plan.currentIndex || 0,
        plan.prompts.length - 1);
    const loadedPrompt = plan.prompts[index]?.prompt?.trim();
    if (plan.stepStatus === "loaded" &&
        loadedPrompt &&
        promptInput.value.trim() === loadedPrompt) {
        promptInput.value = "";
        updateClearInputButton();
    }

    clearBuildPlan();
    status.textContent =
        "Build Plan exited · project and conversation retained";
    promptInput.focus();
}

function renderActiveBuildPlan() {
    const plan = getBuildPlan();
    activeBuildPlanPanel.replaceChildren();
    activeBuildPlanPanel.hidden = !plan;
    if (!plan)
        return;

    const index = Math.min(plan.currentIndex || 0, plan.prompts.length - 1);
    const step = plan.prompts[index];
    const copy = document.createElement("div");
    copy.className = "active-build-plan-copy";
    const label = document.createElement("span");
    label.className = "eyebrow";
    label.textContent = "ACTIVE BUILD PLAN";
    const title = document.createElement("strong");
    title.textContent = plan.completed
        ? "Plan complete"
        : `Prompt ${index + 1} of ${plan.prompts.length} - ${step.title}`;
    const detail = document.createElement("small");
    copy.append(label, title, detail);

    const controls = document.createElement("div");
    controls.className = "active-build-plan-controls";
    const primary = document.createElement("button");
    primary.type = "button";
    primary.className = "button primary";
    const view = document.createElement("button");
    view.type = "button";
    view.className = "button";
    view.textContent = "View plan";
    view.addEventListener("click", async () => {
        const action = await showBuildPlan();
        if (action === "start")
            loadBuildPlanPrompt(getBuildPlan()?.currentIndex || 0);
    });
    const exit = document.createElement("button");
    exit.type = "button";
    exit.className = "build-plan-exit";
    exit.textContent = "Exit plan";
    exit.addEventListener("click", exitBuildPlan);

    const state = plan.stepStatus || "ready";
    if (plan.completed) {
        detail.textContent = "All planned stages have been completed.";
        primary.textContent = "Plan complete";
        primary.disabled = true;
    } else if (state === "loaded") {
        detail.textContent =
            `Prompt ${index + 1} is in the composer. Review it, then press Send.`;
        primary.textContent = `Prompt ${index + 1} loaded - press Send`;
        primary.disabled = true;
    } else if (state === "sent") {
        detail.textContent =
            `Xen is generating the code for Prompt ${index + 1}.`;
        primary.textContent = "Generating current prompt";
        primary.disabled = true;
    } else if (state === "awaiting-clarification") {
        detail.textContent =
            `Xen needs more information for Prompt ${index + 1}. Answer its question below; this step will remain active.`;
        primary.textContent = "Waiting for your answer";
        primary.disabled = true;
    } else if (state === "build-checking") {
        detail.textContent =
            `Xen is checking Prompt ${index + 1} against the installed NinjaTrader assemblies.`;
        primary.textContent = "Running Build Check";
        primary.disabled = true;
    } else if (state === "build-failed") {
        const finalStep = index === plan.prompts.length - 1;
        detail.textContent =
            "Build Check did not pass. Repair the reported errors, or continue only if the server check is incompatible with your local setup.";
        primary.textContent = finalStep
            ? "Continue anyway and finish plan"
            : `Continue anyway to Prompt ${index + 2}`;
        primary.addEventListener("click", () => advanceBuildPlan(plan, index));
    } else if (state === "response-ready") {
        const finalStep = index === plan.prompts.length - 1;
        const toolName = plan.task === "build-indicator"
            ? "Indicator"
            : "Strategy";
        detail.textContent =
            `Import the ${toolName} into NinjaTrader, confirm it compiles, and test the current features before continuing.`;
        primary.textContent = finalStep
            ? "Finish plan"
            : `Load Prompt ${index + 2}`;
        primary.addEventListener("click", () => {
            if (finalStep) {
                plan.completed = true;
                updateBuildPlan(plan);
                return;
            }
            plan.currentIndex = index + 1;
            plan.stepStatus = "ready";
            updateBuildPlan(plan);
            loadBuildPlanPrompt(plan.currentIndex);
        });
    } else if (state === "compiled") {
        const finalStep = index === plan.prompts.length - 1;
        detail.textContent = finalStep
            ? "The final stage compiled successfully."
            : `Prompt ${index + 1} compiled successfully.`;
        primary.textContent = finalStep
            ? "Testing complete - finish plan"
            : `Testing complete - load Prompt ${index + 2}`;
        primary.addEventListener("click", () => {
            if (finalStep) {
                plan.completed = true;
                updateBuildPlan(plan);
                return;
            }
            plan.currentIndex = index + 1;
            plan.stepStatus = "ready";
            updateBuildPlan(plan);
            loadBuildPlanPrompt(plan.currentIndex);
        });
    } else {
        detail.textContent =
            `Load Prompt ${index + 1} into the composer when you are ready.`;
        primary.textContent = `Load Prompt ${index + 1}`;
        primary.addEventListener("click", () => loadBuildPlanPrompt(index));
    }

    controls.appendChild(primary);
    if (state === "response-ready" || plan.completed) {
        const latestCode = getLatestGeneratedCode();
        if (latestCode) {
            const copyCode = document.createElement("button");
            copyCode.type = "button";
            copyCode.className = "button";
            copyCode.textContent = "Copy code";
            copyCode.addEventListener("click", async () => {
                const copied = await copyText(latestCode);
                copyCode.textContent = copied ? "Code copied" : "Copy failed";
                window.setTimeout(
                    () => copyCode.textContent = "Copy code",
                    1600);
            });
            controls.appendChild(copyCode);
        }
    }
    controls.appendChild(view);
    controls.appendChild(exit);
    activeBuildPlanPanel.append(copy, controls);
}

function getLatestGeneratedCode() {
    for (let index = history.length - 1; index >= 0; index -= 1) {
        const turn = history[index];
        if (turn.role === "assistant") {
            const blocks = [...turn.content.matchAll(
                /```(?:csharp|cs)?\s*([\s\S]*?)```/gi)];
            if (blocks.length)
                return blocks.at(-1)[1].trim();
        }

        if (turn.role === "user") {
            const uploadedSource = (turn.content || "").match(
                /^\s*Source file:\s*[^\r\n]+\.(?:cs|txt)\s*\r?\n\s*\r?\n([\s\S]+)$/i);
            if (uploadedSource)
                return uploadedSource[1].trim();
        }
    }
    return "";
}

function extractLatestCodeBlock(text) {
    const blocks = [...(text || "").matchAll(
        /```(?:csharp|cs)?\s*([\s\S]*?)```/gi)];
    return blocks.length ? blocks.at(-1)[1].trim() : "";
}

function isGeneratedRepairPrompt(prompt) {
    return (prompt || "")
        .trimStart()
        .startsWith(generatedRepairPromptPrefix);
}

let requirementsValidationCode = "";

function openRequirementsValidation(code = getLatestGeneratedCode()) {
    if (!code) {
        status.textContent = "No complete NinjaScript source is available to verify";
        return;
    }

    requirementsValidationCode = code;
    requirementsValidationText.value = collectRequirementsForValidation();
    requirementsValidationModel.textContent =
        `Verification uses ${selectedModelDisplayName()} and consumes Xen credit.`;
    requirementsValidationStatus.textContent = "";
    requirementsValidationStatus.classList.remove("error", "success");
    runRequirementsValidationButton.disabled = false;
    runRequirementsValidationButton.textContent = "Run verification";
    requirementsValidationModal.hidden = false;
    document.body.classList.add("modal-open");
    window.setTimeout(() => requirementsValidationText.focus(), 0);
}

function closeRequirementsValidation() {
    if (runRequirementsValidationButton.disabled)
        return;
    requirementsValidationModal.hidden = true;
    document.body.classList.remove("modal-open");
    requirementsValidationCode = "";
}

function collectRequirementsForValidation() {
    const plan = getBuildPlan();
    if (plan)
        return formatBuildPlan(plan);

    const userRequirements = history
        .filter(turn => turn.role === "user")
        .map(turn => turn.content?.trim())
        .filter(Boolean)
        .filter(text => findLikelyCodeStart(text) < 0)
        .join("\n\n---\n\n");

    return userRequirements ||
        "Describe the requirements that the generated code must satisfy.";
}

async function submitRequirementsValidation(event) {
    event.preventDefault();
    const requirements = requirementsValidationText.value.trim();
    if (!requirements || !requirementsValidationCode)
        return;

    runRequirementsValidationButton.disabled = true;
    document.getElementById("cancelRequirementsValidationButton").disabled = true;
    document.getElementById("closeRequirementsValidationButton").disabled = true;
    runRequirementsValidationButton.textContent = "Verifying...";
    requirementsValidationStatus.replaceChildren(
        createWorkingIndicator(
            "Auditing requirements against the latest source...",
            "prompt-builder-loading"));

    try {
        const response = await fetch("/api/requirements/validate", {
            method: "POST",
            headers: {
                "Authorization": `Bearer ${token}`,
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                requirements,
                code: requirementsValidationCode,
                task: activeTask,
                model: modelSelect.value,
                projectId: currentProjectId
            })
        });
        const result = await response.json().catch(() => ({}));
        if (response.status === 401) {
            sessionStorage.removeItem("nx_access_token");
            location.replace("/login.html");
            return;
        }
        if (!response.ok)
            throw new Error(result.detail || result.message ||
                "Requirements verification could not be completed.");

        requirementsValidationModal.hidden = true;
        document.body.classList.remove("modal-open");
        renderRequirementsValidationResult(
            result.message,
            requirements,
            result.model || modelSelect.value);
        updateBalance(result.balanceGbp);
        status.textContent = "Requirements verification complete · saving project...";
        const saved = await saveCurrentProject(requirementsValidationCode);
        status.textContent = saved
            ? "Requirements verification saved"
            : "Verification complete · project not saved";
    } catch (error) {
        requirementsValidationStatus.textContent = error.message;
        requirementsValidationStatus.classList.add("error");
    } finally {
        runRequirementsValidationButton.disabled = false;
        document.getElementById("cancelRequirementsValidationButton").disabled = false;
        document.getElementById("closeRequirementsValidationButton").disabled = false;
        runRequirementsValidationButton.textContent = "Run verification";
    }
}

function renderRequirementsValidationResult(report, requirements, auditModel) {
    const normalizedReport = report.replace(
        /^# Requirements (?:Validation|Verification)\s*/i,
        "").trim();
    const storedReport =
        `# Requirements Verification\n\n${normalizedReport}`;
    history.push({ role: "assistant", content: storedReport });

    const message = addMessage("assistant", "");
    message.classList.add("requirements-validation-message");
    const content = message.querySelector(".message-content");

    const note = document.createElement("div");
    note.className = "requirements-validation-note";
    note.textContent =
        `Requirements audit · ${modelDisplayName(auditModel)}`;
    content.appendChild(note);

    const reportContent = document.createElement("div");
    renderStructuredResponse(reportContent, storedReport);
    content.appendChild(reportContent);

    const actions = document.createElement("div");
    actions.className = "requirements-validation-actions";
    const repair = document.createElement("button");
    repair.type = "button";
    repair.className = "button primary";
    repair.textContent = "Repair missing requirements";
    repair.addEventListener("click", () => {
        if (generating || repair.disabled)
            return;

        repair.disabled = true;
        promptInput.value =
            "Repair the latest complete NinjaScript source to address only the " +
            "explicit requirements classified as Partially implemented or Not " +
            "implemented in the verification report below. Do not implement " +
            "optional enhancements, risks or manual-test suggestions unless they " +
            "are necessary for an explicit missing requirement. Preserve all " +
            "working and unrelated behaviour. Return one complete compile-ready " +
            "C# file.\n\n" +
            `Original requirements:\n${requirements}\n\n` +
            `Verification report:\n${storedReport}`;
        updateClearInputButton();
        status.textContent = "Starting focused repair...";
        form.requestSubmit();
    });
    actions.appendChild(repair);
    content.appendChild(actions);
    scrollMessagesToBottom();
}

function selectedModelDisplayName() {
    return modelSelect.selectedOptions[0]?.textContent?.trim() ||
        modelSelect.value;
}

function modelDisplayName(model) {
    return [...modelSelect.options]
        .find(option => option.value === model)?.textContent?.trim() ||
        model;
}

function openFeedback(options = {}) {
    const promptedByFrustration = options?.promptedByFrustration === true;
    document.getElementById("feedbackType").value =
        promptedByFrustration ? "bug" : "feedback";
    document.getElementById("feedbackIncludeDiagnostics").checked = true;
    feedbackComment.placeholder = promptedByFrustration
        ? "Tell us what went wrong or what you expected Xen to do..."
        : "Describe your feedback or what went wrong...";
    feedbackStatus.textContent = "";
    feedbackStatus.classList.remove("error", "success");
    feedbackModal.hidden = false;
    document.body.classList.add("modal-open");
    window.setTimeout(() => feedbackComment.focus(), 0);
}

function closeFeedback() {
    feedbackModal.hidden = true;
    document.body.classList.remove("modal-open");
    feedbackForm.reset();
    document.getElementById("feedbackIncludeDiagnostics").checked = true;
    feedbackComment.placeholder =
        "Describe your feedback or what went wrong...";
    submitFeedbackButton.disabled = false;
    submitFeedbackButton.textContent = "Send report";
    feedbackStatus.textContent = "";
    feedbackStatus.classList.remove("error", "success");
}

function isClearlyFrustrated(prompt) {
    const text = String(prompt || "")
        .replace(/```[\s\S]*?```/g, " ")
        .replace(/\s+/g, " ")
        .trim();
    if (!text)
        return false;

    return [
        /\b(fuck|fucking|shit|bullshit|crap|useless|stupid|idiot|garbage|rubbish)\b/i,
        /\b(this|that|you|xen|model|code|response)\s+(is|are)\s+(wrong|useless|terrible|awful|broken)\b/i,
        /\b(doesn['’]?t|does not|didn['’]?t|did not)\s+(work|listen|follow|understand)\b/i,
        /\bwhy\s+(the hell|won['’]?t|doesn['’]?t|does not)\b/i
    ].some(pattern => pattern.test(text));
}

function appendFeedbackInvitation(message) {
    const invitationKey =
        `nx_feedback_invitation_${currentProjectId || "session"}`;
    if (sessionStorage.getItem(invitationKey))
        return;
    sessionStorage.setItem(invitationKey, "shown");

    const invitation = document.createElement("div");
    invitation.className = "feedback-invitation";

    const copy = document.createElement("div");
    copy.className = "feedback-invitation-copy";
    const title = document.createElement("strong");
    title.textContent = "Help us improve Xen";
    const description = document.createElement("span");
    description.textContent =
        "It looks like this result may not have met your expectations. Tell us what went wrong so we can help.";
    copy.append(title, description);

    const actions = document.createElement("div");
    actions.className = "feedback-invitation-actions";
    const feedback = document.createElement("button");
    feedback.type = "button";
    feedback.className = "button primary";
    feedback.textContent = "Provide feedback";
    feedback.addEventListener("click", () => {
        invitation.remove();
        openFeedback({ promptedByFrustration: true });
    });
    const dismiss = document.createElement("button");
    dismiss.type = "button";
    dismiss.className = "button";
    dismiss.textContent = "Not now";
    dismiss.addEventListener("click", () => invitation.remove());
    actions.append(feedback, dismiss);
    invitation.append(copy, actions);

    const content = message.querySelector(".message-content");
    (content || message).appendChild(invitation);
}

async function submitFeedback(event) {
    event.preventDefault();
    const comment = feedbackComment.value.trim();
    if (comment.length < 5) {
        feedbackStatus.textContent =
            "Please provide a little more detail before sending.";
        feedbackStatus.classList.add("error");
        return;
    }

    const includeDiagnostics =
        document.getElementById("feedbackIncludeDiagnostics").checked;
    const latestUserPrompt = getLatestHistoryContent("user");
    const latestAssistantOutput = getLatestHistoryContent("assistant");

    submitFeedbackButton.disabled = true;
    submitFeedbackButton.textContent = "Sending...";
    feedbackStatus.textContent = "";
    feedbackStatus.classList.remove("error", "success");

    try {
        const response = await fetch("/api/feedback", {
            method: "POST",
            headers: {
                "Authorization": `Bearer ${token}`,
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                type: document.getElementById("feedbackType").value,
                comment,
                includeDiagnostics,
                projectId: includeDiagnostics ? currentProjectId : null,
                model: includeDiagnostics ? modelSelect.value : null,
                task: includeDiagnostics ? activeTask : null,
                appVersion: includeDiagnostics
                    ? document.querySelector(".workspace-build-version")
                        ?.textContent.replace(/^Build\s+/i, "").trim()
                    : null,
                userPrompt: includeDiagnostics ? latestUserPrompt : null,
                assistantOutput: includeDiagnostics
                    ? latestAssistantOutput
                    : null,
                latestCode: includeDiagnostics
                    ? getLatestGeneratedCode()
                    : null
            })
        });

        if (response.status === 401) {
            sessionStorage.removeItem("nx_access_token");
            location.replace("/login.html");
            return;
        }

        const payload = await response.json().catch(() => ({}));
        if (!response.ok)
            throw new Error(payload.detail || payload.message ||
                "Your report could not be sent.");

        feedbackStatus.textContent =
            "Thank you. Your report has been sent to ClickAlgo support.";
        feedbackStatus.classList.add("success");
        submitFeedbackButton.textContent = "Sent";
        window.setTimeout(closeFeedback, 1800);
    } catch (error) {
        submitFeedbackButton.disabled = false;
        submitFeedbackButton.textContent = "Send report";
        feedbackStatus.textContent =
            error.message || "Your report could not be sent.";
        feedbackStatus.classList.add("error");
    }
}

function getLatestHistoryContent(role) {
    for (let index = history.length - 1; index >= 0; index -= 1) {
        if (history[index].role === role &&
            history[index].content?.trim()) {
            return history[index].content.trim();
        }
    }
    return null;
}

function loadBuildPlanPrompt(index) {
    const plan = getBuildPlan();
    const step = plan?.prompts?.[index];
    if (!step)
        return;

    activeTask = plan.task;
    document.querySelectorAll(".task-button").forEach(item =>
        item.classList.toggle("active", item.dataset.task === activeTask));
    document.getElementById("taskTitle").textContent = taskNames[activeTask];
    promptInput.placeholder = taskPlaceholders[activeTask];
    updateTaskSpecificUi(true);

    plan.currentIndex = index;
    plan.stepStatus = "loaded";
    delete plan.activePrompt;
    plan.completed = false;
    updateBuildPlan(plan);
    promptInput.value = step.prompt;
    updateClearInputButton();
    promptInput.focus();
    promptInput.scrollIntoView({ behavior: "smooth", block: "center" });
}

function markBuildPlanPromptSent(prompt) {
    const plan = getBuildPlan();
    if (!plan || plan.completed)
        return;
    const index = Math.min(plan.currentIndex || 0, plan.prompts.length - 1);
    const isClarificationAnswer =
        plan.stepStatus === "awaiting-clarification";
    if (!isClarificationAnswer &&
        plan.prompts[index]?.prompt?.trim() !== prompt?.trim())
        return;
    plan.projectId = currentProjectId;
    plan.activePrompt = prompt.trim();
    plan.stepStatus = "sent";
    updateBuildPlan(plan);
}

function markBuildPlanResponseReady(prompt) {
    const plan = getBuildPlan();
    if (!plan || plan.completed || plan.stepStatus !== "sent")
        return false;
    const index = Math.min(plan.currentIndex || 0, plan.prompts.length - 1);
    const expectedPrompt = plan.activePrompt ||
        plan.prompts[index]?.prompt?.trim();
    if (expectedPrompt !== prompt?.trim())
        return false;
    plan.stepStatus = "response-ready";
    updateBuildPlan(plan);
    return true;
}

function setBuildPlanStepStatus(stepStatus) {
    const plan = getBuildPlan();
    if (!plan || plan.completed)
        return;
    plan.stepStatus = stepStatus;
    updateBuildPlan(plan);
}

function advanceBuildPlan(plan, index) {
    const finalStep = index === plan.prompts.length - 1;
    if (finalStep) {
        plan.completed = true;
        updateBuildPlan(plan);
        return;
    }
    plan.currentIndex = index + 1;
    plan.stepStatus = "ready";
    delete plan.activePrompt;
    updateBuildPlan(plan);
    loadBuildPlanPrompt(plan.currentIndex);
}

function restoreBuildPlanPromptLoaded(prompt) {
    const plan = getBuildPlan();
    if (!plan || plan.stepStatus !== "sent")
        return;
    const index = Math.min(plan.currentIndex || 0, plan.prompts.length - 1);
    const plannedPrompt = plan.prompts[index]?.prompt?.trim();
    const expectedPrompt = plan.activePrompt || plannedPrompt;
    if (expectedPrompt !== prompt?.trim())
        return;
    plan.stepStatus = plan.activePrompt === plannedPrompt
        ? "loaded"
        : "awaiting-clarification";
    updateBuildPlan(plan);
}

function markBuildPlanCompileSucceeded() {
    const plan = getBuildPlan();
    if (!plan || plan.completed)
        return;
    plan.stepStatus = "compiled";
    updateBuildPlan(plan);
}

function isSavedBuildPlanPrompt(prompt) {
    return Boolean(getBuildPlan()?.prompts?.some(
        step => step.prompt?.trim() === prompt?.trim()));
}

function buildRetrievalPrompt(prompt) {
    const plan = getBuildPlan();
    if (!plan?.originalPrompt || !isSavedBuildPlanPrompt(prompt))
        return prompt;

    return `${plan.originalPrompt}\n\nCurrent Build Plan step:\n${prompt}`;
}

async function showBuildPlan(plan = getBuildPlan()) {
    if (!plan)
        return "close";

    openPromptBuilder();
    document.getElementById("promptBuilderTitle").textContent =
        "Your NinjaTrader Build Plan";
    promptBuilderBody.classList.remove("waiting");
    promptBuilderModal.querySelector(".prompt-builder-dialog")
        ?.classList.add("prompt-builder-plan-dialog");
    promptBuilderReason.textContent = plan.explanation ||
        "Build and test this project in small, ordered iterations.";
    promptBuilderBody.replaceChildren();

    const instruction = document.createElement("p");
    instruction.className = "build-plan-instruction";
    instruction.textContent =
        "Submit prompts in order. Compile and test every stage in NinjaTrader before continuing.";
    promptBuilderBody.appendChild(instruction);

    if (plan.assumptions?.length) {
        const assumptions = document.createElement("section");
        assumptions.className = "build-plan-assumptions";
        const title = document.createElement("h3");
        title.textContent = "Material assumptions";
        const list = document.createElement("ul");
        plan.assumptions.forEach(text => {
            const item = document.createElement("li");
            item.textContent = text;
            list.appendChild(item);
        });
        assumptions.append(title, list);
        promptBuilderBody.appendChild(assumptions);
    }

    plan.prompts.forEach((step, index) => {
        const card = document.createElement("section");
        card.className = "build-plan-step";
        if (index === plan.currentIndex)
            card.classList.add("current");

        const heading = document.createElement("div");
        heading.className = "build-plan-step-heading";
        const title = document.createElement("h3");
        title.textContent = `Prompt ${index + 1} - ${step.title}`;
        const text = document.createElement("pre");
        text.textContent = step.prompt;
        heading.appendChild(title);
        card.append(heading, text);
        promptBuilderBody.appendChild(card);
    });

    promptBuilderStatus.textContent = "";
    promptBuilderStatus.classList.remove("error");
    promptBuilderActions.replaceChildren();
    addPlanAction(
        plan.currentIndex === 0 ? "Start with Prompt 1" :
            `Reload Prompt ${plan.currentIndex + 1}`,
        "start",
        "button primary");
    return waitForPromptBuilderDecision();
}

function addPlanAction(label, decision, className) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = className;
    button.textContent = label;
    button.addEventListener("click", () => closePromptBuilder(decision));
    promptBuilderActions.appendChild(button);
}

function formatBuildPlan(plan) {
    const assumptions = plan.assumptions?.length
        ? `\nMaterial assumptions\n${plan.assumptions.map(item => `- ${item}`).join("\n")}\n`
        : "";
    const prompts = plan.prompts.map((step, index) =>
        `Prompt ${index + 1} - ${step.title}\n\n${step.prompt}`)
        .join("\n\n---\n\n");
    return `NinjaTrader Xen Build Plan\n\n${plan.explanation}${assumptions}\n${prompts}\n\n` +
        "Submit prompts in order and compile/test every stage before continuing.";
}

async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}

function renderPromptQuestions(questions) {
    promptBuilderBody.classList.remove("waiting");
    promptBuilderBody.replaceChildren();
    (questions || []).forEach((item, index) => {
        const field = document.createElement("label");
        field.className = "prompt-builder-field";

        const label = document.createElement("span");
        label.textContent = `${index + 1}. ${item.label}`;

        const question = document.createElement("small");
        question.textContent = item.question;

        const input = document.createElement("textarea");
        input.rows = 2;
        input.maxLength = 2000;
        input.placeholder = item.placeholder || "Enter your preference";
        input.dataset.question = item.question;
        input.addEventListener("input", updatePromptBuilderComposeState);

        field.append(label, question, input);
        promptBuilderBody.appendChild(field);
    });
}

function createReviewSummary() {
    const summary = document.createElement("div");
    summary.className = "prompt-review-summary";
    const title = document.createElement("strong");
    title.textContent = "Would you like Prompt Builder to plan it first?";
    const detail = document.createElement("p");
    detail.textContent = pendingImage
        ? "Your image will remain attached. Use Prompt Builder to add requirements for more control, or build directly from the image and let Xen infer the indicator."
        : "It will ask only the important missing requirements, then create an ordered sequence of small, testable prompts.";
    summary.append(title, detail);
    return summary;
}

function setPromptBuilderActions(actions) {
    promptBuilderActions.replaceChildren();
    actions.forEach(([label, decision, className]) => {
        const button = document.createElement("button");
        button.type = "button";
        button.className = className;
        button.textContent = label;
        button.dataset.decision = decision;
        button.addEventListener("click", () => {
            if (decision === "compose" && !validatePromptBuilderAnswers())
                return;
            closePromptBuilder(decision);
        });
        promptBuilderActions.appendChild(button);
    });
}

function updatePromptBuilderComposeState() {
    const fields = [...promptBuilderBody.querySelectorAll("[data-question]")];
    if (promptBuilderStatus.dataset.validation !== "answers")
        return;

    fields.forEach(field => field.closest(".prompt-builder-field")
        ?.classList.toggle("invalid", !field.value.trim()));
    const allAnswered = fields.length > 0 &&
        fields.every(field => field.value.trim());
    if (allAnswered) {
        promptBuilderStatus.textContent = "";
        promptBuilderStatus.classList.remove("error");
        delete promptBuilderStatus.dataset.validation;
    }
}

function validatePromptBuilderAnswers() {
    const fields = [...promptBuilderBody.querySelectorAll("[data-question]")];
    const unanswered = fields.filter(field => !field.value.trim());
    if (fields.length > 0 && !unanswered.length)
        return true;

    fields.forEach(field => field.closest(".prompt-builder-field")
        ?.classList.toggle("invalid", !field.value.trim()));
    promptBuilderStatus.textContent =
        "Answer every question before creating the Build Plan, or use Xen's baseline suggestions.";
    promptBuilderStatus.classList.add("error");
    promptBuilderStatus.dataset.validation = "answers";
    unanswered[0]?.focus();
    return false;
}

function openPromptBuilder() {
    promptBuilderModal.hidden = false;
    document.body.classList.add("modal-open");
}

function closePromptBuilder(decision) {
    if (promptBuilderModal.hidden)
        return;
    promptBuilderModal.hidden = true;
    document.body.classList.remove("modal-open");
    const resolve = resolvePromptBuilder;
    resolvePromptBuilder = null;
    resolve?.(decision);
}

function waitForPromptBuilderDecision() {
    return new Promise(resolve => {
        resolvePromptBuilder = resolve;
    });
}

function setPromptBuilderBusy(busy, message = "") {
    promptBuilderBody.querySelectorAll("textarea, button").forEach(
        element => element.disabled = busy);
    promptBuilderActions.querySelectorAll("button").forEach(
        element => element.disabled = busy);
    if (message)
        setPromptBuilderLoading(message);
}

function setPromptBuilderLoading(message) {
    promptBuilderStatus.classList.remove("error");
    promptBuilderStatus.replaceChildren(
        createWorkingIndicator(message, "prompt-builder-loading"));
}

function showPromptBuilderWait(message) {
    openPromptBuilder();
    promptBuilderReason.textContent = "";
    promptBuilderBody.classList.add("waiting");
    promptBuilderBody.replaceChildren(
        createWorkingIndicator(message, "prompt-builder-loading"));
    promptBuilderStatus.textContent = "";
    promptBuilderStatus.classList.remove("error");
    promptBuilderActions.replaceChildren();
}

function setComposerReviewState(reviewing, message) {
    sendButton.disabled = reviewing;
    modelSelect.disabled = reviewing;
    promptInput.disabled = reviewing;
    updateClearInputButton();
    if (reviewing) {
        status.replaceChildren(
            createWorkingIndicator(message, "composer-review-loading"));
    } else {
        status.textContent = message;
    }
    if (!reviewing)
        applyCreditAvailability();
}

function createWorkingIndicator(message, extraClass) {
    const indicator = document.createElement("span");
    indicator.className = `working-indicator ${extraClass}`;

    for (let index = 0; index < 3; index += 1) {
        const dot = document.createElement("span");
        dot.className = "working-dot";
        dot.setAttribute("aria-hidden", "true");
        indicator.appendChild(dot);
    }

    const label = document.createElement("strong");
    label.textContent = message;
    indicator.appendChild(label);
    return indicator;
}

async function promptBuilderFetch(url, body) {
    const response = await fetch(url, {
        method: "POST",
        headers: {
            "Authorization": `Bearer ${token}`,
            "Content-Type": "application/json"
        },
        body: JSON.stringify(body)
    });

    if (response.status === 401) {
        sessionStorage.removeItem("nx_access_token");
        location.replace("/login.html");
        throw new Error("Your session has expired.");
    }

    const payload = await response.json().catch(() => ({}));
    if (!response.ok)
        throw new Error(payload.detail || payload.message ||
            "Prompt Builder is temporarily unavailable.");
    return payload;
}

function createProjectTitle(prompt) {
    const compact = prompt.replace(/\s+/g, " ").trim();
    return compact.length <= 54 ? compact : `${compact.slice(0, 51).trim()}…`;
}

function updateProjectTitle() {
    document.getElementById("projectTitle").textContent =
        currentProjectTitle || "Unsaved project";
}

function updateHistoryButton() {
    codeViewButton.disabled = !hasProjectSnapshots;
    codeViewButton.title = hasProjectSnapshots
        ? "View saved source snapshots"
        : "History is available after Xen saves a source snapshot";
}

async function saveCurrentProject(code = "") {
    if (!currentProjectId || history.length < 2)
        return false;

    // Build Check and requirements reports are assistant turns without code.
    // Resolve the newest source across the conversation so saving a report
    // still snapshots the code response that immediately preceded it.
    const latestCode = looksLikeCompleteNinjaScript(code) ? code.trim() : "";
    if (isNewCodeBuildTask() && !latestCode)
        return false;

    history = compactPreflightBuildHistory(history);

    try {
        const response = await fetch("/api/projects", {
            method: "POST",
            headers: {
                "Authorization": `Bearer ${token}`,
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                projectId: currentProjectId,
                title: currentProjectTitle,
                task: activeTask,
                model: modelSelect.value,
                messages: history,
                latestCode: latestCode || null
            })
        });

        if (response.ok) {
            currentProjectPersisted = true;
            sessionStorage.setItem(activeProjectStorageKey, currentProjectId);
            if (latestCode) {
                hasProjectSnapshots = true;
                updateHistoryButton();
            }
        }
        return response.ok;
    } catch {
        return false;
    }
}

async function openCodeWorkspace() {
    codeWorkspaceModal.hidden = false;
    document.body.classList.add("modal-open");
    revisionList.replaceChildren();
    revisionMessage.textContent = "";
    revisionMessage.classList.remove("error", "success");
    revisionPage = 1;

    if (!currentProjectId) {
        setCodeWorkspaceCode("", "No saved source");
        setCodeWorkspacePrompt("");
        revisionMessage.textContent =
            "Generate code or open a saved project to view its source history.";
        document.getElementById("revisionCount").textContent = "0 versions";
        document.getElementById("revisionControls").hidden = true;
        return;
    }

    const currentCode = getLatestGeneratedCode();
    if (currentCode)
        setCodeWorkspaceCode(currentCode, "Current generated source");
    else
        setCodeWorkspaceCode("", "Loading source...");

    setCodeWorkspacePrompt(null, true);
    await loadCodeRevisionPage(1, { previewCurrent: true });
}

async function loadCodeRevisionPage(page = revisionPage, options = {}) {
    revisionMessage.textContent = options.message || "Loading snapshots...";
    revisionMessage.classList.remove("error", "success");
    try {
        const response = await fetch(
            `/api/projects/${currentProjectId}/revisions?page=${page}`,
            { headers: { "Authorization": `Bearer ${token}` } });
        if (response.status === 401) {
            sessionStorage.removeItem("nx_access_token");
            location.replace("/login.html");
            return;
        }
        if (!response.ok)
            throw new Error("Unable to load source snapshots.");

        const result = await response.json();
        if (!(result.revisions || []).length && result.totalCount > 0 &&
            page > result.totalPages) {
            await loadCodeRevisionPage(result.totalPages, options);
            return;
        }
        revisionPage = result.page || 1;
        revisionTotalPages = result.totalPages || 1;
        renderCodeRevisions(result);
        if (options.previewCurrent && result.revisions?.length)
            await previewCodeRevision(result.revisions[0]);
        if (options.message)
            showRevisionMessage(options.message, options.messageType || "success");
    } catch (error) {
        showRevisionMessage(error.message, "error");
    }
}

function closeCodeWorkspace() {
    codeWorkspaceModal.hidden = true;
    document.body.classList.remove("modal-open");
    revisionMessage.classList.remove("error", "success");
}

function renderCodeRevisions(result) {
    const revisions = Array.isArray(result.revisions) ? result.revisions : [];
    hasProjectSnapshots = (result.totalCount ?? revisions.length) > 0;
    updateHistoryButton();
    revisionList.replaceChildren();
    revisionMessage.textContent = revisions.length
        ? ""
        : "No source snapshots have been saved for this project yet.";
    revisionMessage.classList.remove("error", "success");
    if (!revisions.length)
        setCodeWorkspacePrompt("");
    const totalCount = result.totalCount ?? revisions.length;
    const pinnedCount = result.pinnedCount || 0;
    document.getElementById("revisionCount").textContent =
        `${totalCount} ${totalCount === 1 ? "snapshot" : "snapshots"} · ` +
        `${pinnedCount} pinned`;
    document.getElementById("revisionControls").hidden = totalCount <= revisionPageSize;
    document.getElementById("revisionPageLabel").textContent =
        `Page ${revisionPage} of ${revisionTotalPages}`;
    document.getElementById("previousRevisionPageButton").disabled =
        revisionPage <= 1;
    document.getElementById("nextRevisionPageButton").disabled =
        revisionPage >= revisionTotalPages;

    revisions.forEach(revision => {
        const row = document.createElement("article");
        row.className = "revision-item";
        if (revision.isCurrent)
            row.classList.add("current");
        if (revision.isPinned)
            row.classList.add("pinned");
        row.dataset.revisionId = String(revision.revisionId);

        const details = document.createElement("button");
        details.type = "button";
        details.className = "revision-details";
        const title = document.createElement("strong");
        title.textContent = `v${revision.versionNumber}`;
        const badge = document.createElement("span");
        badge.textContent = revision.isCurrent ? "Current" : revision.model;
        const date = document.createElement("small");
        date.textContent = new Intl.DateTimeFormat("en-GB", {
            dateStyle: "medium",
            timeStyle: "short"
        }).format(new Date(revision.createdUtc));
        title.appendChild(badge);
        details.append(title, date);
        details.addEventListener(
            "click",
            () => previewCodeRevision(revision));

        const actions = document.createElement("div");
        actions.className = "revision-actions";
        const pin = document.createElement("button");
        pin.type = "button";
        pin.className = "revision-action";
        pin.textContent = revision.isPinned ? "Unpin" : "Pin";
        pin.addEventListener(
            "click",
            () => setCodeRevisionPin(revision, !revision.isPinned));
        const restore = document.createElement("button");
        restore.type = "button";
        restore.className = "revision-action";
        restore.textContent = revision.isCurrent ? "Current" : "Restore";
        restore.disabled = revision.isCurrent;
        if (!revision.isCurrent) {
            restore.addEventListener(
                "click",
                () => restoreCodeRevision(revision));
        }

        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "revision-action danger";
        remove.textContent = "Delete";
        remove.disabled = revision.isCurrent || revision.isPinned;
        if (!remove.disabled)
            remove.addEventListener("click", () => deleteCodeRevision(revision));
        actions.append(pin, restore, remove);
        row.append(details, actions);
        if (revision.isPinned) {
            const pinnedMarker = document.createElement("span");
            pinnedMarker.className = "revision-pinned-marker";
            pinnedMarker.textContent = "Pinned";
            row.appendChild(pinnedMarker);
        }
        revisionList.appendChild(row);
    });
}

function showRevisionMessage(message, type = "") {
    revisionMessage.textContent = message;
    revisionMessage.classList.remove("error", "success");
    if (type)
        revisionMessage.classList.add(type);
}

async function setCodeRevisionPin(revision, pinned) {
    try {
        const response = await fetch(
            `/api/projects/${currentProjectId}/revisions/${revision.revisionId}/pin`,
            {
                method: "PATCH",
                headers: {
                    "Authorization": `Bearer ${token}`,
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({ pinned })
            });
        const result = await response.json().catch(() => ({}));
        if (!response.ok)
            throw new Error(result.message || "Unable to update this snapshot.");
        await loadCodeRevisionPage(revisionPage, {
            message: pinned ? "Snapshot pinned." : "Snapshot unpinned."
        });
    } catch (error) {
        showRevisionMessage(error.message, "error");
    }
}

async function deleteCodeRevision(revision) {
    if (!window.confirm(
        `Delete v${revision.versionNumber}? This snapshot cannot be recovered.`)) {
        return;
    }

    try {
        const response = await fetch(
            `/api/projects/${currentProjectId}/revisions/${revision.revisionId}`,
            {
                method: "DELETE",
                headers: { "Authorization": `Bearer ${token}` }
            });
        const result = await response.json().catch(() => ({}));
        if (!response.ok)
            throw new Error(result.message || "Unable to delete this snapshot.");
        await loadCodeRevisionPage(revisionPage, {
            message: `v${revision.versionNumber} was deleted.`
        });
    } catch (error) {
        showRevisionMessage(error.message, "error");
    }
}

async function deleteUnpinnedCodeRevisions() {
    const confirmation = window.prompt(
        "Delete every unpinned snapshot except the current one? " +
        "Pinned snapshots will be kept. Type DELETE to continue.");
    if (confirmation?.trim().toUpperCase() !== "DELETE")
        return;

    try {
        const response = await fetch(
            `/api/projects/${currentProjectId}/revisions`,
            {
                method: "DELETE",
                headers: {
                    "Authorization": `Bearer ${token}`,
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({ confirmation })
            });
        const result = await response.json().catch(() => ({}));
        if (!response.ok)
            throw new Error(result.message || "Unable to delete snapshot history.");
        const deleted = result.deletedSnapshots || 0;
        await loadCodeRevisionPage(1, {
            message: `${deleted} unpinned ${deleted === 1 ? "snapshot" : "snapshots"} deleted.`
        });
    } catch (error) {
        showRevisionMessage(error.message, "error");
    }
}

async function previewCodeRevision(revision) {
    revisionMessage.textContent = "Loading source...";
    try {
        const response = await fetch(
            `/api/projects/${currentProjectId}/revisions/${revision.revisionId}`,
            { headers: { "Authorization": `Bearer ${token}` } });
        if (!response.ok)
            throw new Error("Unable to load this source snapshot.");
        const result = await response.json();
        setCodeWorkspaceCode(
            result.code,
            `v${revision.versionNumber} - ${formatRevisionDate(result.createdUtc)}`);
        setCodeWorkspacePrompt(result.prompt);
        revisionMessage.textContent = "";
        revisionList.querySelectorAll(".revision-item").forEach(item =>
            item.classList.remove("selected"));
        const selected = [...revisionList.children]
            .find(item => item.dataset.revisionId === String(revision.revisionId));
        selected?.classList.add("selected");
    } catch (error) {
        revisionMessage.textContent = error.message;
        revisionMessage.classList.add("error");
    }
}

async function restoreCodeRevision(revision) {
    if (!window.confirm(
        `Restore v${revision.versionNumber} as the current project source? ` +
        "Your existing snapshots will remain available.")) {
        return;
    }

    revisionMessage.textContent = `Restoring v${revision.versionNumber}...`;
    try {
        const response = await fetch(
            `/api/projects/${currentProjectId}/revisions/${revision.revisionId}/restore`,
            {
                method: "POST",
                headers: { "Authorization": `Bearer ${token}` }
            });
        const result = await response.json().catch(() => ({}));
        if (!response.ok)
            throw new Error(result.detail || "Unable to restore this snapshot.");

        applyProjectToWorkspace(result);
        clearBuildPlan();
        setCodeWorkspaceCode(result.latestCode, "Restored current source");
        revisionMessage.textContent =
            `v${revision.versionNumber} was restored as a new current snapshot. ` +
            "The active Build Plan was cleared because it referred to a newer source state.";
        revisionMessage.classList.add("success");

        await loadCodeRevisionPage(1, {
            previewCurrent: true,
            message:
                `v${revision.versionNumber} was restored as a new current snapshot. ` +
                "The active Build Plan was cleared because it referred to a newer source state."
        });
    } catch (error) {
        revisionMessage.textContent = error.message;
        revisionMessage.classList.add("error");
    }
}

function setCodeWorkspacePrompt(prompt, loading = false) {
    codeWorkspacePrompt.textContent = loading
        ? "Loading the prompt used to generate this snapshot..."
        : (prompt?.trim() ||
            "The generating prompt is unavailable for this older snapshot.");
}

function setCodeWorkspaceCode(code, meta) {
    codeWorkspaceCode = code?.trim() || "";
    const codeElement = document.createElement("code");
    if (codeWorkspaceCode) {
        codeElement.className = "language-csharp";
        codeElement.innerHTML = highlightCSharp(codeWorkspaceCode);
    } else {
        codeElement.textContent =
            "Select a saved project with generated code.";
    }
    codeWorkspacePreview.replaceChildren(codeElement);

    const classMatch = codeWorkspaceCode.match(
        /\bclass\s+([A-Za-z_][A-Za-z0-9_]*)/);
    document.getElementById("codeWorkspaceFileName").textContent =
        `${classMatch ? classMatch[1] : "NinjaScript"}.cs`;
    document.getElementById("codeWorkspaceMeta").textContent = meta;
    document.getElementById("copyWorkspaceCodeButton").disabled =
        !codeWorkspaceCode;
    document.getElementById("downloadWorkspaceCodeButton").disabled =
        !codeWorkspaceCode;
}

async function copyWorkspaceCode() {
    if (!codeWorkspaceCode)
        return;
    const button = document.getElementById("copyWorkspaceCodeButton");
    const copied = await copyText(codeWorkspaceCode);
    button.textContent = copied ? "Code copied" : "Copy failed";
    window.setTimeout(() => button.textContent = "Copy code", 1500);
}

function formatRevisionDate(value) {
    const date = new Date(value);
    return Number.isNaN(date.getTime())
        ? "Saved snapshot"
        : new Intl.DateTimeFormat("en-GB", {
            dateStyle: "medium",
            timeStyle: "short"
        }).format(date);
}

async function openProjects() {
    projectsModal.hidden = false;
    document.body.classList.add("modal-open");
    projectsList.innerHTML = "";
    document.getElementById("projectsMessage").textContent = "Loading projects…";

    try {
        const response = await fetch("/api/projects", {
            headers: { "Authorization": `Bearer ${token}` }
        });
        if (!response.ok)
            throw new Error("Unable to load projects.");

        const result = await response.json();
        renderProjects(result.projects || []);
    } catch (error) {
        document.getElementById("projectsMessage").textContent = error.message;
    }
}

function closeProjects() {
    projectsModal.hidden = true;
    document.body.classList.remove("modal-open");
}

function renderProjects(projects) {
    const projectMessage = document.getElementById("projectsMessage");
    const projectsFooter = document.getElementById("projectsFooter");
    const deleteAllButton = document.getElementById("deleteAllProjectsButton");
    projectsList.innerHTML = "";
    projectsFooter.hidden = projects.length === 0;
    deleteAllButton.dataset.projectCount = String(projects.length);
    deleteAllButton.disabled = false;

    if (!projects.length) {
        projectMessage.textContent = "No saved projects yet.";
        return;
    }

    projectMessage.textContent = "";
    for (const project of projects) {
        const row = document.createElement("article");
        row.className = "project-row";
        if (project.projectId === currentProjectId)
            row.classList.add("current");

        const main = document.createElement("button");
        main.type = "button";
        main.className = "project-open";
        main.innerHTML = `
            <strong>${escapeHtml(project.title)}</strong>
            <span>${escapeHtml(taskNames[project.task] || project.task)} ·
                ${new Date(project.updatedUtc).toLocaleString()}</span>
        `;
        main.addEventListener("click", () => loadProject(project.projectId));

        const actions = document.createElement("div");
        actions.className = "project-actions";

        const open = document.createElement("button");
        open.type = "button";
        open.textContent = "Open";
        open.className = "open";
        open.setAttribute(
            "aria-label",
            `Open project ${project.title}`);
        open.addEventListener(
            "click",
            () => loadProject(project.projectId));

        const rename = document.createElement("button");
        rename.type = "button";
        rename.textContent = "Rename";
        rename.addEventListener("click", () => renameProject(project));

        const exportButton = document.createElement("button");
        exportButton.type = "button";
        exportButton.textContent = "Export";
        exportButton.setAttribute(
            "aria-label",
            `Export conversation ${project.title}`);
        exportButton.addEventListener(
            "click",
            () => exportProject(project));

        const remove = document.createElement("button");
        remove.type = "button";
        remove.textContent = "Delete";
        remove.className = "danger";
        remove.addEventListener("click", () => deleteProject(project));

        actions.append(open, rename, exportButton, remove);
        row.append(main, actions);
        projectsList.appendChild(row);
    }
}

async function exportProject(project) {
    const projectMessage = document.getElementById("projectsMessage");
    try {
        const response = await fetch(`/api/projects/${project.projectId}`, {
            headers: { "Authorization": `Bearer ${token}` }
        });
        if (!response.ok)
            throw new Error("Unable to export the project.");

        const savedProject = await response.json();
        const taskName = taskNames[savedProject.task] || savedProject.task;
        const messages = (savedProject.messages || []).map(turn => {
            const speaker = turn.role === "user" ? "You" : "Xen";
            const roleClass = turn.role === "user" ? "user" : "xen";
            const exportedContent =
                turn.role === "user" && isAnalyzerRequest(turn.content)
                    ? [
                        "Analyse Strategy Analyzer results",
                        `Files: ${analyzerFileNamesFromRequest(turn.content)
                            .join(", ")}`,
                        analyzerContextFromRequest(turn.content)
                    ].filter(Boolean).join("\n\n")
                    : turn.content?.trim() || "";
            return `
                <article class="message ${roleClass}">
                    <h2>${speaker}</h2>
                    <pre>${escapeHtml(exportedContent)}</pre>
                </article>`;
        }).join("");

        const exported = `<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>${escapeHtml(savedProject.title)} - Xen conversation</title>
    <style>
        :root { color-scheme: dark; }
        * { box-sizing: border-box; }
        body {
            margin: 0;
            background: #101010;
            color: #e7e7e7;
            font: 15px/1.65 Arial, sans-serif;
        }
        main { width: min(960px, calc(100% - 32px)); margin: 40px auto; }
        header { border-bottom: 1px solid #353535; padding-bottom: 24px; }
        h1 { margin: 0 0 8px; font-size: 28px; }
        .meta { margin: 0; color: #aaa; }
        .message { border-bottom: 1px solid #2c2c2c; padding: 24px 0; }
        .message h2 {
            margin: 0 0 10px;
            color: #ff4b2b;
            font-size: 12px;
            letter-spacing: .08em;
            text-transform: uppercase;
        }
        .message.user h2 { color: #8cced7; }
        pre {
            margin: 0;
            color: inherit;
            font: inherit;
            overflow-wrap: anywhere;
            white-space: pre-wrap;
        }
        @media print {
            :root { color-scheme: light; }
            body { background: #fff; color: #111; }
            main { width: 100%; margin: 0; }
            .message, header { border-color: #ccc; }
        }
    </style>
</head>
<body>
    <main>
        <header>
            <h1>${escapeHtml(savedProject.title)}</h1>
            <p class="meta">Task: ${escapeHtml(taskName)}<br>
                Exported: ${escapeHtml(new Date().toLocaleString())}</p>
        </header>
        ${messages || '<p class="message">No conversation messages were saved.</p>'}
    </main>
</body>
</html>`;

        const blob = new Blob(
            [exported],
            { type: "text/html;charset=utf-8" });
        const link = document.createElement("a");
        link.href = URL.createObjectURL(blob);
        link.download =
            `${safeExportFileName(savedProject.title)}-conversation.html`;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(link.href);
        projectMessage.textContent = "Conversation exported";
    } catch (error) {
        projectMessage.textContent = error.message;
    }
}

function safeExportFileName(title) {
    return (title || "xen-project")
        .replace(/[<>:"/\\|?*\u0000-\u001f]/g, "-")
        .replace(/\s+/g, "-")
        .replace(/-+/g, "-")
        .replace(/^-|-$/g, "")
        .slice(0, 80) || "xen-project";
}

async function loadProject(projectId) {
    if (generating)
        return;

    try {
        const response = await fetch(`/api/projects/${projectId}`, {
            headers: { "Authorization": `Bearer ${token}` }
        });
        if (!response.ok)
            throw new Error("Unable to open the project.");

        const project = await response.json();
        applyProjectToWorkspace(project);
        sessionStorage.setItem(activeProjectStorageKey, project.projectId);
        await loadExistingCodeState();
        status.textContent = "Project loaded";
        closeProjects();
        scrollMessagesToBottom();
    } catch (error) {
        document.getElementById("projectsMessage").textContent = error.message;
    }
}

async function restoreActiveProject() {
    const projectId = sessionStorage.getItem(activeProjectStorageKey);
    if (!projectId)
        return;

    try {
        const response = await fetch(`/api/projects/${projectId}`, {
            headers: { "Authorization": `Bearer ${token}` }
        });
        if (response.status === 401) {
            sessionStorage.removeItem("nx_access_token");
            location.replace("/login.html");
            return;
        }
        if (response.status === 404) {
            sessionStorage.removeItem(activeProjectStorageKey);
            return;
        }
        if (!response.ok)
            return;

        const project = await response.json();
        applyProjectToWorkspace(project);
        await loadExistingCodeState();
        scrollMessagesToBottom();
    } catch {
        // Keep the active project identity so a transient failure can recover
        // on the next workspace load.
    }
}

function applyProjectToWorkspace(project) {
    currentProjectId = project.projectId;
    currentProjectTitle = project.title;
    currentProjectPersisted = true;
    hasProjectSnapshots = Boolean(project.latestCode);
    updateHistoryButton();
    activeTask = taskNames[project.task] ? project.task : "build-strategy";
    history = compactPreflightBuildHistory(
        Array.isArray(project.messages) ? project.messages : []);
    promptInput.value = "";
    clearAnalyzerExports(false);
    updateClearInputButton();

    document.querySelectorAll(".task-button").forEach(item =>
        item.classList.toggle("active", item.dataset.task === activeTask));
    document.getElementById("taskTitle").textContent = taskNames[activeTask];
    promptInput.placeholder = taskPlaceholders[activeTask];
    updateTaskSpecificUi();

    modelSelect.value =
        [...modelSelect.options].some(option => option.value === project.model)
            ? project.model
            : defaultModel;
    acceptedModelSelection = modelSelect.value;
    rememberSelectedModel();
    updateModelCostBadge();
    updateImageUploadUi();

    messages.innerHTML = "";
    for (const turn of history) {
        const message = addMessage(turn.role, "");
        const content = message.querySelector(".message-content");
        if (turn.role === "assistant" &&
            activeTask === "analyse-backtest" &&
            isBacktestReportSource(turn.content)) {
            message.classList.add("backtest-report-message");
            renderBacktestReport(content, turn.content);
        } else if (turn.role === "assistant") {
            renderStructuredResponse(content, turn.content);
        } else {
            renderUserMessage(content, turn.content);
        }
    }

    if (!history.length)
        addMessage("assistant", taskIntro(activeTask));
    updateProjectTitle();
    renderActiveBuildPlan();
}

async function renameProject(project) {
    const proposed = window.prompt("Project name", project.title);
    const title = proposed?.replace(/\s+/g, " ").trim();
    if (!title || title === project.title)
        return;

    const response = await fetch(`/api/projects/${project.projectId}`, {
        method: "PATCH",
        headers: {
            "Authorization": `Bearer ${token}`,
            "Content-Type": "application/json"
        },
        body: JSON.stringify({ title })
    });

    if (response.ok) {
        if (project.projectId === currentProjectId) {
            currentProjectTitle = title;
            updateProjectTitle();
        }
        await openProjects();
    }
}

async function deleteProject(project) {
    if (!window.confirm(`Delete “${project.title}”? This cannot be undone.`))
        return;

    const response = await fetch(`/api/projects/${project.projectId}`, {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${token}` }
    });

    if (response.ok) {
        if (project.projectId === currentProjectId)
            startNewProject();
        await openProjects();
    }
}

async function deleteAllProjects() {
    const button = document.getElementById("deleteAllProjectsButton");
    const projectCount = Number(button.dataset.projectCount) || 0;
    if (projectCount === 0)
        return;

    const confirmation = window.prompt(
        `Permanently delete all ${projectCount} saved project${
            projectCount === 1 ? "" : "s"}?\n\n` +
        "This also deletes every source revision and cannot be undone. " +
        "Type DELETE to continue."
    );
    if (confirmation?.trim().toUpperCase() !== "DELETE")
        return;

    button.disabled = true;
    const projectMessage = document.getElementById("projectsMessage");
    projectMessage.textContent = "Deleting all projectsâ€¦";

    try {
        const response = await fetch("/api/projects/all", {
            method: "DELETE",
            headers: { "Authorization": `Bearer ${token}` }
        });
        if (response.status === 401) {
            sessionStorage.removeItem("nx_access_token");
            location.replace("/login.html");
            return;
        }
        if (!response.ok)
            throw new Error("Unable to delete all projects.");

        startNewProject(false);
        await openProjects();
        projectMessage.textContent = "All saved projects were deleted.";
    } catch (error) {
        projectMessage.textContent = error.message;
        button.disabled = false;
    }
}

async function loadBalance() {
    try {
        const response = await fetch("/api/account/summary", {
            headers: { "Authorization": `Bearer ${token}` }
        });
        if (!response.ok)
            return;
        const result = await response.json();
        updateBalance(result.balanceGbp);
        if (Number(result.balanceGbp) <= 0)
            showNoTrialCreditNotice(result.subscriberId);
    } catch {
        document.getElementById("workspaceBalance").textContent = "Credit unavailable";
    }
}

function updateBalance(value) {
    const balance = Math.max(0, Number(value) || 0);
    const balanceElement = document.getElementById("workspaceBalance");
    currentBalanceGbp = balance;
    balanceElement.textContent =
        `Credit: ${new Intl.NumberFormat("en-GB", {
            style: "currency",
            currency: "GBP",
            minimumFractionDigits: 2,
            maximumFractionDigits: 4
        }).format(balance)}`;

    balanceElement.classList.remove("low", "empty");
    if (balance <= 0) {
        balanceElement.classList.add("empty");
        balanceElement.title = "Credit exhausted — top up to continue";
    } else if (balance <= lowCreditThresholdGbp) {
        balanceElement.classList.add("low");
        balanceElement.title = "Low credit — top up soon";
    } else {
        balanceElement.title = "";
    }

    applyCreditAvailability();
}

function applyCreditAvailability() {
    const exhausted = currentBalanceGbp !== null &&
        currentBalanceGbp <= 0;

    if (exhausted) {
        sendButton.disabled = true;
        promptInput.disabled = true;
        updateClearInputButton();
        promptInput.placeholder =
            "Your Xen credit has run out. Please top up to continue.";
        if (!generating)
            status.textContent = "Credit exhausted · top up to continue";
        return;
    }

    if (!generating) {
        sendButton.disabled = false;
        promptInput.disabled = false;
        promptInput.placeholder = taskPlaceholders[activeTask];
        updateClearInputButton();
    }
}

function restoreSelectedModel() {
    const savedModel = localStorage.getItem("nx_selected_model");
    if (savedModel &&
        [...modelSelect.options].some(option => option.value === savedModel)) {
        modelSelect.value = savedModel;
        return;
    }

    modelSelect.value = defaultModel;
    if (savedModel)
        localStorage.removeItem("nx_selected_model");
}

function rememberSelectedModel() {
    localStorage.setItem("nx_selected_model", modelSelect.value);
}

function updateModelCostBadge() {
    modelCostBadge.hidden = !lowCostModels.has(modelSelect.value);
}

// Reserved integration point for NinjaTrader Xen's future real compile/build
// validation workflow. Nothing in the chat response path dispatches this event.
window.addEventListener(
    "ninjatrader:build-validation-succeeded",
    markBuildPlanCompileSucceeded);
