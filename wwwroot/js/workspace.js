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

const lowCreditThresholdGbp = 1;

const messages = document.getElementById("messages");
const form = document.getElementById("chatForm");
const promptInput = document.getElementById("promptInput");
const sendButton = document.getElementById("sendButton");
const cancelButton = document.getElementById("cancelButton");
const status = document.getElementById("chatStatus");
const modelSelect = document.getElementById("modelSelect");
const projectsModal = document.getElementById("projectsModal");
const projectsList = document.getElementById("projectsList");

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

document.getElementById("projectsButton").addEventListener("click", openProjects);
document.getElementById("closeProjectsButton").addEventListener("click", closeProjects);
document.getElementById("newProjectButton").addEventListener("click", () => {
    if (generating)
        return;
    startNewProject();
});

projectsModal.addEventListener("click", event => {
    if (event.target === projectsModal)
        closeProjects();
});

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

    const prompt = promptInput.value.trim();
    if (!prompt)
        return;

    const createdProjectForRequest = !currentProjectId;
    if (createdProjectForRequest) {
        currentProjectId = crypto.randomUUID();
        currentProjectTitle = createProjectTitle(prompt);
        updateProjectTitle();
    }

    const previousHistory = [...history];
    const userMessage = addMessage("user", prompt);
    history.push({ role: "user", content: prompt });
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
        assistantMessage.classList.remove("generating");
        renderStructuredResponse(content, assistantText);
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

            if (createdProjectForRequest) {
                currentProjectId = null;
                currentProjectTitle = "";
                updateProjectTitle();
            }

            status.textContent = "Cancelled";
            return;
        }

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
