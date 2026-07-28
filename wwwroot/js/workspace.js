const token = sessionStorage.getItem("nx_access_token");
if (!token)
    location.replace("/login.html");

const taskNames = {
    "build-strategy": "Build Strategy",
    "build-indicator": "Build Indicator",
    "existing-strategy": "Existing Strategy",
    "existing-indicator": "Existing Indicator"
};

const taskPlaceholders = {
    "build-strategy": "Describe your NinjaTrader strategy…",
    "build-indicator": "Describe your NinjaTrader indicator…",
    "existing-strategy": "Paste your strategy code and describe the changes…",
    "existing-indicator": "Paste your indicator code and describe the changes…"
};

let activeTask = "build-strategy";
let history = [];
let generating = false;
let currentProjectId = null;
let currentProjectTitle = "";
let currentController = null;
let currentBalanceGbp = null;
let promptQualityChecked = false;

const lowCreditThresholdGbp = 1;
const buildPlanStorageKey = "nx_active_build_plan_v1";

const messages = document.getElementById("messages");
const form = document.getElementById("chatForm");
const promptInput = document.getElementById("promptInput");
const sendButton = document.getElementById("sendButton");
const cancelButton = document.getElementById("cancelButton");
const status = document.getElementById("chatStatus");
const modelSelect = document.getElementById("modelSelect");
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

restoreSelectedModel();
modelSelect.addEventListener("change", rememberSelectedModel);
cancelButton.addEventListener("click", () => {
    if (!currentController)
        return;

    cancelButton.disabled = true;
    status.textContent = "Cancelling…";
    currentController.abort();
});
loadBalance();
showTrialWelcome();
renderActiveBuildPlan();

document.getElementById("projectsButton").addEventListener("click", openProjects);
document.getElementById("closeProjectsButton").addEventListener("click", closeProjects);
document.getElementById("newProjectButton").addEventListener("click", () => {
    if (generating)
        return;
    clearBuildPlan();
    startNewProject();
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
    if (!button || generating)
        return;

    activeTask = button.dataset.task;
    document.querySelectorAll(".task-button").forEach(item =>
        item.classList.toggle("active", item === button));
    document.getElementById("taskTitle").textContent = taskNames[activeTask];
    promptInput.placeholder = taskPlaceholders[activeTask];
    startNewProject(false);
    applyCreditAvailability();
});

form.addEventListener("submit", async event => {
    event.preventDefault();
    if (generating)
        return;

    if (currentBalanceGbp !== null && currentBalanceGbp <= 0) {
        applyCreditAvailability();
        status.textContent = "Credit exhausted · top up to continue";
        return;
    }

    let prompt = promptInput.value.trim();
    if (!prompt)
        return;

    const reviewedPrompt = await reviewInitialBuildPrompt(prompt);
    if (!reviewedPrompt)
        return;
    prompt = reviewedPrompt;

    const createdProjectForRequest = !currentProjectId;
    if (createdProjectForRequest) {
        currentProjectId = crypto.randomUUID();
        currentProjectTitle = createProjectTitle(prompt);
        updateProjectTitle();
    }

    const previousHistory = [...history];
    const userMessage = addMessage("user", prompt);
    history.push({ role: "user", content: prompt });
    markBuildPlanPromptSent(prompt);
    promptInput.value = "";

    const assistantMessage = addMessage("assistant", "");
    const content = assistantMessage.querySelector(".message-content");
    assistantMessage.classList.add("generating");
    content.innerHTML = `
        <div class="working-indicator">
            <span class="working-dot"></span>
            <span class="working-dot"></span>
            <span class="working-dot"></span>
            <strong>Xen is generating your NinjaScript…</strong>
        </div>
    `;

    generating = true;
    sendButton.disabled = true;
    sendButton.classList.add("loading");
    cancelButton.disabled = false;
    modelSelect.disabled = true;
    promptInput.disabled = true;
    status.textContent = "Working…";
    scrollMessagesToBottom();
    currentController = new AbortController();

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
                history: previousHistory
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
        markBuildPlanResponseReady(prompt);
        assistantMessage.classList.remove("generating");
        renderStructuredResponse(content, assistantText);
        if (ragDebug)
            appendRagDebug(assistantMessage, ragDebug);
        scrollMessagesToBottom();
        status.textContent = "Saving project…";
        const saved = await saveCurrentProject();
        status.textContent = saved ? "Saved" : "Response ready · project not saved";
    } catch (error) {
        if (error.name === "AbortError") {
            history = previousHistory;
            userMessage.remove();
            assistantMessage.remove();
            promptInput.value = prompt;
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
    content.textContent = role === "user"
        ? formatUserMessage(text)
        : text;

    article.append(label, content);
    messages.appendChild(article);
    scrollMessagesToBottom();
    return article;
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

function appendRagDebug(message, debug) {
    const element = document.createElement("div");
    element.className = debug.used
        ? "rag-debug"
        : "rag-debug rejected";
    const confidence = Math.round(
        Math.max(0, Math.min(1, Number(debug.similarity) || 0)) * 100);
    element.textContent = debug.used
        ? `Knowledge match: ${debug.title} · Confidence ${confidence}%`
        : `Knowledge match not used: ${debug.title} · Confidence ${confidence}%`;
    message.appendChild(element);
}

function scrollMessagesToBottom() {
    messages.scrollTo({
        top: messages.scrollHeight,
        behavior: "smooth"
    });
}

function renderStructuredResponse(container, source) {
    container.textContent = "";
    const fencePattern = /```(?:csharp|cs)?\s*([\s\S]*?)```/gi;
    let cursor = 0;
    let match;

    while ((match = fencePattern.exec(source)) !== null) {
        appendProse(container, source.slice(cursor, match.index));
        appendCodeBlock(container, match[1].trim());
        cursor = match.index + match[0].length;
    }

    appendProse(container, source.slice(cursor));
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

        const heading = line.match(/^(#{1,3})\s+(.+)$/);
        if (heading) {
            const element = document.createElement(
                heading[1].length === 1 ? "h2" : "h3");
            element.textContent = heading[2];
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
            item.textContent = bullet[1];
            list.appendChild(item);
            continue;
        }

        const paragraph = document.createElement("p");
        paragraph.textContent = line.replace(/\*\*(.*?)\*\*/g, "$1");
        container.appendChild(paragraph);
        list = null;
    }
}

function appendCodeBlock(container, code) {
    const wrapper = document.createElement("section");
    wrapper.className = "code-block";

    const toolbar = document.createElement("div");
    toolbar.className = "code-toolbar";

    const language = document.createElement("span");
    language.textContent = "C# · NinjaScript";

    const actions = document.createElement("div");
    actions.className = "code-actions";

    const copyButton = document.createElement("button");
    copyButton.type = "button";
    copyButton.className = "code-action";
    copyButton.textContent = "Copy";
    copyButton.addEventListener("click", async () => {
        await navigator.clipboard.writeText(code);
        copyButton.textContent = "Copied";
        setTimeout(() => copyButton.textContent = "Copy", 1200);
    });

    const downloadButton = document.createElement("button");
    downloadButton.type = "button";
    downloadButton.className = "code-action";
    downloadButton.textContent = "Download .cs";
    downloadButton.addEventListener("click", () => downloadCode(code));

    actions.append(copyButton, downloadButton);
    toolbar.append(language, actions);

    const pre = document.createElement("pre");
    const codeElement = document.createElement("code");
    codeElement.className = "language-csharp";
    codeElement.innerHTML = highlightCSharp(code);
    pre.appendChild(codeElement);

    wrapper.append(toolbar, pre);
    container.appendChild(wrapper);
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
        "existing-indicator": "Paste the complete indicator source and explain exactly what should change."
    };
    return intros[task];
}

function startNewProject(resetTask = true) {
    currentProjectId = null;
    currentProjectTitle = "";
    history = [];
    promptQualityChecked = false;

    if (resetTask) {
        activeTask = "build-strategy";
        document.querySelectorAll(".task-button").forEach(item =>
            item.classList.toggle("active", item.dataset.task === activeTask));
        document.getElementById("taskTitle").textContent = taskNames[activeTask];
        promptInput.placeholder = taskPlaceholders[activeTask];
    }

    messages.innerHTML = "";
    addMessage("assistant", taskIntro(activeTask));
    updateProjectTitle();
    status.textContent = "Ready";
    promptInput.focus();
}

let resolvePromptBuilder = null;

async function reviewInitialBuildPrompt(prompt) {
    if (isSavedBuildPlanPrompt(prompt)) {
        promptQualityChecked = true;
        return prompt;
    }

    if (promptQualityChecked ||
        currentProjectId ||
        history.length > 0 ||
        !["build-strategy", "build-indicator"].includes(activeTask)) {
        return prompt;
    }

    setComposerReviewState(true, "Reviewing your request...");
    try {
        const response = await promptBuilderFetch("/api/prompt-builder/check", {
            task: activeTask,
            prompt
        });

        if (!response.recommendPromptBuilder) {
            promptQualityChecked = true;
            return prompt;
        }

        const decision = await showPromptReview(response.reason);
        if (decision === "build") {
            promptQualityChecked = true;
            return prompt;
        }
        if (decision !== "clarify")
            return null;

        const improvedPrompt = await runPromptBuilder(prompt);
        if (!improvedPrompt)
            return null;

        promptInput.value = improvedPrompt;
        promptQualityChecked = true;
        return improvedPrompt;
    } catch {
        promptQualityChecked = true;
        return prompt;
    } finally {
        setComposerReviewState(false, "Ready");
    }
}

function showPromptReview(reason) {
    document.querySelector(".prompt-builder-dialog")
        ?.classList.remove("prompt-builder-plan-dialog");
    document.getElementById("promptBuilderTitle").textContent =
        "Plan this build first?";
    promptBuilderReason.textContent = reason ||
        "A few details could materially improve the generated NinjaScript.";
    promptBuilderBody.replaceChildren(createReviewSummary());
    promptBuilderStatus.textContent = "";
    setPromptBuilderActions([
        ["Plan with Prompt Builder", "clarify", "button primary"],
        ["Build in Xen anyway", "build", "button"],
        ["Edit original request", "edit", "button"]
    ]);
    openPromptBuilder();
    return waitForPromptBuilderDecision();
}

async function runPromptBuilder(prompt) {
    document.getElementById("promptBuilderTitle").textContent =
        "Clarify your build request";
    promptBuilderReason.textContent =
        "Answer the relevant questions. Leave an answer blank when you want Xen to use a sensible configurable default.";
    showPromptBuilderWait("Preparing clarification questions...");

    try {
        const result = await promptBuilderFetch("/api/prompt-builder/questions", {
            task: activeTask,
            prompt
        });
        renderPromptQuestions(result.questions);
        promptBuilderStatus.textContent = "";
        setPromptBuilderActions([
            ["Create Build Plan", "compose", "button primary"],
            ["Build original request", "build", "button"],
            ["Edit request", "edit", "button"]
        ]);

        const decision = await waitForPromptBuilderDecision();
        if (decision === "build") {
            promptQualityChecked = true;
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
            prompt,
            answers
        });
        const plan = saveBuildPlan(planResult, activeTask);
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
            ["Build original request", "build", "button primary"],
            ["Edit request", "edit", "button"]
        ]);
        const fallback = await waitForPromptBuilderDecision();
        return fallback === "build" ? prompt : null;
    } finally {
        setPromptBuilderBusy(false);
    }
}

function saveBuildPlan(result, task) {
    const plan = {
        version: 2,
        task,
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
        return plan && Array.isArray(plan.prompts) && plan.prompts.length
            ? plan
            : null;
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
    activeBuildPlanPanel.append(copy, controls);
}

function getLatestGeneratedCode() {
    for (let index = history.length - 1; index >= 0; index -= 1) {
        const turn = history[index];
        if (turn.role !== "assistant")
            continue;
        const blocks = [...turn.content.matchAll(
            /```(?:csharp|cs)?\s*([\s\S]*?)```/gi)];
        if (blocks.length)
            return blocks.at(-1)[1].trim();
    }
    return "";
}

function openFeedback() {
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
    submitFeedbackButton.disabled = false;
    submitFeedbackButton.textContent = "Send report";
    feedbackStatus.textContent = "";
    feedbackStatus.classList.remove("error", "success");
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

    plan.currentIndex = index;
    plan.stepStatus = "loaded";
    plan.completed = false;
    updateBuildPlan(plan);
    promptInput.value = step.prompt;
    promptQualityChecked = true;
    promptInput.focus();
    promptInput.scrollIntoView({ behavior: "smooth", block: "center" });
}

function markBuildPlanPromptSent(prompt) {
    const plan = getBuildPlan();
    if (!plan || plan.completed)
        return;
    const index = Math.min(plan.currentIndex || 0, plan.prompts.length - 1);
    if (plan.prompts[index]?.prompt?.trim() !== prompt?.trim())
        return;
    plan.stepStatus = "sent";
    updateBuildPlan(plan);
}

function markBuildPlanResponseReady(prompt) {
    const plan = getBuildPlan();
    if (!plan || plan.completed || plan.stepStatus !== "sent")
        return;
    const index = Math.min(plan.currentIndex || 0, plan.prompts.length - 1);
    if (plan.prompts[index]?.prompt?.trim() !== prompt?.trim())
        return;
    plan.stepStatus = "response-ready";
    updateBuildPlan(plan);
}

function restoreBuildPlanPromptLoaded(prompt) {
    const plan = getBuildPlan();
    if (!plan || plan.stepStatus !== "sent")
        return;
    const index = Math.min(plan.currentIndex || 0, plan.prompts.length - 1);
    if (plan.prompts[index]?.prompt?.trim() !== prompt?.trim())
        return;
    plan.stepStatus = "loaded";
    updateBuildPlan(plan);
}

function markBuildPlanCompileSucceeded() {
    const plan = getBuildPlan();
    if (!plan || plan.completed || plan.stepStatus !== "sent")
        return;
    plan.stepStatus = "compiled";
    updateBuildPlan(plan);
}

function isSavedBuildPlanPrompt(prompt) {
    return Boolean(getBuildPlan()?.prompts?.some(
        step => step.prompt?.trim() === prompt?.trim()));
}

async function showBuildPlan(plan = getBuildPlan()) {
    if (!plan)
        return "close";

    openPromptBuilder();
    document.getElementById("promptBuilderTitle").textContent =
        "Your NinjaTrader Build Plan";
    promptBuilderBody.classList.remove("waiting");
    document.querySelector(".prompt-builder-dialog")
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
        const copy = document.createElement("button");
        copy.type = "button";
        copy.className = "button build-plan-copy";
        copy.textContent = "Copy prompt";
        copy.addEventListener("click", async () => {
            const copied = await copyText(step.prompt);
            copy.textContent = copied ? "Copied" : "Copy failed";
            window.setTimeout(() => copy.textContent = "Copy prompt", 1600);
        });
        const text = document.createElement("pre");
        text.textContent = step.prompt;
        heading.append(title, copy);
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
    addPlanUtilityAction("Copy All", async button => {
        const copied = await copyText(formatBuildPlan(plan));
        button.textContent = copied ? "Copied All" : "Copy failed";
    });
    addPlanUtilityAction("Download Plan", () => downloadBuildPlan(plan));
    addPlanAction("Close", "close", "button");
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

function addPlanUtilityAction(label, action) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "button";
    button.textContent = label;
    button.addEventListener("click", () => action(button));
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

function downloadBuildPlan(plan) {
    const blob = new Blob([formatBuildPlan(plan)], {
        type: "text/plain;charset=utf-8"
    });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `ninjatrader-xen-build-plan-${new Date().toISOString().slice(0, 10)}.txt`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
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
    detail.textContent =
        "It will ask only the important missing requirements, then create an ordered sequence of small, testable prompts.";
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
        button.addEventListener("click", () => closePromptBuilder(decision));
        promptBuilderActions.appendChild(button);
    });
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

async function saveCurrentProject() {
    if (!currentProjectId || history.length < 2)
        return false;

    const latestAssistant = [...history]
        .reverse()
        .find(turn => turn.role === "assistant")?.content || "";
    const codeMatch = latestAssistant.match(/```(?:csharp|cs)?\s*([\s\S]*?)```/i);

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
                latestCode: codeMatch ? codeMatch[1].trim() : null
            })
        });

        return response.ok;
    } catch {
        return false;
    }
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
    projectsList.innerHTML = "";

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

        const remove = document.createElement("button");
        remove.type = "button";
        remove.textContent = "Delete";
        remove.className = "danger";
        remove.addEventListener("click", () => deleteProject(project));

        actions.append(open, rename, remove);
        row.append(main, actions);
        projectsList.appendChild(row);
    }
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
        currentProjectId = project.projectId;
        currentProjectTitle = project.title;
        activeTask = taskNames[project.task] ? project.task : "build-strategy";
        history = Array.isArray(project.messages) ? project.messages : [];

        document.querySelectorAll(".task-button").forEach(item =>
            item.classList.toggle("active", item.dataset.task === activeTask));
        document.getElementById("taskTitle").textContent = taskNames[activeTask];
        promptInput.placeholder = taskPlaceholders[activeTask];

        if ([...modelSelect.options].some(option => option.value === project.model))
            modelSelect.value = project.model;
        rememberSelectedModel();

        messages.innerHTML = "";
        for (const turn of history) {
            const message = addMessage(turn.role, "");
            const content = message.querySelector(".message-content");
            if (turn.role === "assistant")
                renderStructuredResponse(content, turn.content);
            else
                content.textContent = formatUserMessage(turn.content);
        }

        if (!history.length)
            addMessage("assistant", taskIntro(activeTask));

        updateProjectTitle();
        status.textContent = "Project loaded";
        closeProjects();
        scrollMessagesToBottom();
    } catch (error) {
        document.getElementById("projectsMessage").textContent = error.message;
    }
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

async function loadBalance() {
    try {
        const response = await fetch("/api/account/summary", {
            headers: { "Authorization": `Bearer ${token}` }
        });
        if (!response.ok)
            return;
        const result = await response.json();
        updateBalance(result.balanceGbp);
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
    }
}

function restoreSelectedModel() {
    const savedModel = localStorage.getItem("nx_selected_model");
    if (savedModel &&
        [...modelSelect.options].some(option => option.value === savedModel)) {
        modelSelect.value = savedModel;
    }
}

function rememberSelectedModel() {
    localStorage.setItem("nx_selected_model", modelSelect.value);
}

// Reserved integration point for NinjaTrader Xen's future real compile/build
// validation workflow. Nothing in the chat response path dispatches this event.
window.addEventListener(
    "ninjatrader:build-validation-succeeded",
    markBuildPlanCompileSucceeded);
