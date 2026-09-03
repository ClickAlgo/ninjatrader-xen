// Run with: node --test "NinjaTrader Xen.Tests/workspace-request-lifecycle.test.cjs"
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../wwwroot/js/workspace.js'), 'utf8');
function section(start, end) {
    const from = source.indexOf(start);
    const to = source.indexOf(end, from);
    assert.ok(from >= 0 && to > from, `Missing source section: ${start}`);
    return source.slice(from, to);
}
const code = 'namespace NinjaTrader.NinjaScript.Indicators { public class Example : Indicator {} }';
const assistantText = '```csharp\n' + code + '\n```';
const flush = () => new Promise(resolve => setImmediate(resolve));

test('unfenced complete NinjaScript becomes the latest copyable source', () => {
    const completeCode = `using NinjaTrader.NinjaScript;
namespace NinjaTrader.NinjaScript.Indicators
{
    public class RelativeVolume : Indicator
    {
        protected override void OnStateChange() { }
    }
}`;
    const context = {
        history: [
            { role: 'assistant', content: '```csharp\nold code\n```' },
            { role: 'assistant', content: completeCode }
        ],
        looksLikeCompleteNinjaScript(value) {
            return value.includes('class RelativeVolume : Indicator') &&
                value.includes('OnStateChange');
        }
    };
    vm.runInNewContext(
        section('function getLatestGeneratedCode()', 'function isGeneratedRepairPrompt'),
        context);

    assert.equal(context.getLatestGeneratedCode(), completeCode);
    assert.equal(context.extractLatestCodeBlock(completeCode), completeCode);
});

test('unfenced explanations and incomplete source are not treated as code', () => {
    const context = {
        looksLikeCompleteNinjaScript(value) {
            return value.includes('class RelativeVolume : Indicator') &&
                value.includes('OnStateChange');
        }
    };
    vm.runInNewContext(
        section('function extractLatestCodeBlock', 'function isGeneratedRepairPrompt'),
        context);

    assert.equal(context.extractLatestCodeBlock(
        'Here is the change: class RelativeVolume : Indicator { void OnStateChange() {} }'), '');
    assert.equal(context.extractLatestCodeBlock(
        'public class RelativeVolume : Indicator { void OnStateChange() { }'), '');
});
function element() {
    const classes = new Set();
    return {
        disabled: false, value: '', textContent: '', innerHTML: '', removed: false,
        classList: {
            add: name => classes.add(name), remove: name => classes.delete(name),
            contains: name => classes.has(name)
        },
        setAttribute() {}, removeAttribute() {}, focus() {},
        remove() { this.removed = true; }
    };
}

function workspace(options = {}) {
    const timers = new Map();
    const requests = [];
    const messageList = [];
    let timerId = 0;
    let submit;
    const c = {
        AbortController, TextDecoder, console,
        generating: false, preparingRequest: false, preflightBuilding: false,
        currentController: null, currentBalanceGbp: options.balance ?? 1,
        currentProjectId: 'project-1', currentProjectTitle: 'Example',
        currentProjectPersisted: true, hasProjectSnapshots: false,
        activeTask: 'build-indicator', history: [], pendingImage: null,
        pendingAnalyzerExports: [], promptBuilderBypassed: false, promptReviewCompleted: false,
        token: 'test-only', activeProjectStorageKey: 'test-only',
        taskPlaceholders: { 'build-indicator': 'Describe indicator' },
        window: {
            setTimeout(callback, delay) { timers.set(++timerId, { callback, delay }); return timerId; },
            clearTimeout(id) { timers.delete(id); }
        },
        sessionStorage: { setItem() {}, removeItem() {} },
        location: { replace() {} },
        form: { addEventListener(_, handler) { submit = handler; } },
        isExistingCodeTask: () => false,
        redirectMismatchedExistingSource: () => false,
        isClearlyFrustrated: () => false,
        reviewBuildPrompt: async prompt => prompt,
        isGeneratedRepairPrompt: () => false,
        buildRetrievalPrompt: prompt => prompt,
        extractLatestCodeBlock: () => code,
        looksLikeCompleteNinjaScript: value => value === code,
        markBuildPlanResponseReady: () => options.automatic !== false,
        getTaskSwitchTargetFromResponse: () => null,
        isNewCodeBuildTask: () => true,
        compactPreflightBuildHistory: history => history,
        renderStructuredResponse(content, text) { content.textContent = text; },
        renderPreflightBuildResult(result) {
            c.report = result;
            c.history.push({ role: 'assistant', content: 'Build passed' });
        },
        setBuildPlanStepStatus() {
            if (options.storageError) throw new Error('Storage unavailable');
        },
        addMessage(role, text) {
            const message = element();
            message.content = element();
            message.content.textContent = text;
            message.button = element();
            message.querySelector = selector => selector === '.message-content'
                ? message.content : message.button;
            messageList.push(message);
            return message;
        },
        async fetch(url, init) {
            requests.push({ url, signal: init.signal });
            if (url === '/api/chat/stream') {
                if (options.chatPending) return new Promise((_, reject) => {
                    init.signal.addEventListener('abort', () => {
                        const error = new Error('Cancelled');
                        error.name = 'AbortError';
                        reject(error);
                    });
                });
                let sent = false;
                return { ok: true, status: 200, body: { getReader: () => ({
                    async read() {
                        if (sent) return { done: true };
                        sent = true;
                        return { done: false, value: Buffer.from('data: ' + JSON.stringify({
                            type: 'response.output_text.delta', delta: assistantText
                        }) + '\n\ndata: [DONE]\n\n') };
                    }
                }) } };
            }
            if (url === '/api/preflight/build') {
                if (options.buildPending) return new Promise(() => {});
                return { ok: true, status: 200, json: () => options.bodyPending
                    ? new Promise(() => {}) : Promise.resolve({ success: true }) };
            }
            assert.equal(url, '/api/projects');
            if (options.savePending) return new Promise(resolve => { c.finishSave = resolve; });
            if (options.saveReject) throw new Error('Network unavailable');
            return { ok: !options.saveHttpError };
        }
    };
    for (const name of ['sendButton', 'cancelButton', 'modelSelect', 'promptInput', 'status'])
        c[name] = element();
    c.promptInput.value = 'Build an indicator';
    c.modelSelect.value = 'gpt-5.3-codex';
    for (const name of ['markBuildPlanPromptSent', 'clearPendingImage', 'clearAnalyzerExports',
        'updateClearInputButton', 'updateImageUploadUi', 'scrollMessagesToBottom',
        'addModelFeedbackControls', 'renderExistingCodeAttachments', 'updateHistoryButton',
        'restoreBuildPlanPromptLoaded']) c[name] = () => {};
    vm.createContext(c);
    vm.runInContext([
        section('async function withRequestTimeout(', 'async function openCodeWorkspace('),
        section('async function runPreflightBuild(', 'function renderPreflightBuildResult('),
        section('function applyCreditAvailability()', 'function restoreSelectedModel()'),
        section('form.addEventListener("submit",', 'function addMessage(')
    ].join('\n'), c);
    return {
        c, requests, messageList, timers,
        submit: () => submit({ preventDefault() {} }),
        expire(delay) {
            const entry = [...timers].find(([, timer]) => timer.delay === delay);
            assert.ok(entry, `No pending ${delay}ms timer`);
            timers.delete(entry[0]);
            entry[1].callback();
        }
    };
}

function assertReleased(w, disabled = false) {
    assert.equal(w.c.generating, false);
    assert.equal(w.c.preflightBuilding, false);
    assert.equal(w.c.promptInput.disabled, disabled);
    assert.equal(w.c.sendButton.classList.contains('loading'), false);
    assert.equal(w.c.currentController, null);
    assert.equal(w.messageList[1].button.classList.contains('is-checking'), false);
    assert.equal(w.messageList[1].content.textContent, assistantText);
    assert.equal(w.messageList[1].removed, false);
    assert.equal(w.timers.size, 0);
}

test('successful Build Check followed by stalled save unlocks without losing code', async () => {
    const w = workspace({ savePending: true });
    const task = w.submit();
    await flush();
    assert.equal(w.c.report.success, true);
    assert.equal(w.c.generating, true);
    assert.equal(w.c.promptInput.disabled, true);
    assert.equal(w.c.sendButton.classList.contains('loading'), true);
    w.expire(30_000);
    await task;
    assertReleased(w);
    assert.match(w.c.status.textContent, /built.*project not saved.*retry/i);
    assert.equal(w.requests.at(-1).signal.aborted, true);
    // A late response must not mutate the next project's state.
    w.c.currentProjectId = 'project-2';
    w.c.currentProjectPersisted = false;
    w.c.finishSave({ ok: true });
    await flush();
    assert.equal(w.c.currentProjectPersisted, false);
});

for (const kind of ['buildPending', 'bodyPending']) {
    test(`${kind}: Build Check deadline covers headers and JSON body`, async () => {
        const w = workspace({ [kind]: true });
        const task = w.submit();
        await flush();
        w.expire(210_000);
        await task;
        assertReleased(w);
        assert.equal(w.requests.find(r => r.url === '/api/preflight/build').signal.aborted, true);
        assert.match(w.c.status.textContent, /Build Add-On unavailable.*saved/);
    });
}

for (const options of [{}, { saveReject: true }, { saveHttpError: true }, { storageError: true }]) {
    test(`cleanup with ${JSON.stringify(options)}`, async () => {
        const w = workspace(options);
        await w.submit();
        assertReleased(w);
        if (options.saveReject || options.saveHttpError)
            assert.match(w.c.status.textContent, /project not saved/);
    });
}

test('ordinary response save timeout uses the same cleanup', async () => {
    const w = workspace({ automatic: false, savePending: true });
    const task = w.submit();
    await flush();
    w.expire(30_000);
    await task;
    assertReleased(w);
    assert.equal(w.requests.some(r => r.url === '/api/preflight/build'), false);
});

test('cleanup still respects exhausted credit', async () => {
    const w = workspace();
    const originalRender = w.c.renderPreflightBuildResult;
    w.c.renderPreflightBuildResult = result => { originalRender(result); w.c.currentBalanceGbp = 0; };
    await w.submit();
    assertReleased(w, true);
    assert.match(w.c.status.textContent, /Credit exhausted/);
});

test('generation setup exception is protected by outer cleanup', async () => {
    const w = workspace();
    let first = true;
    w.c.scrollMessagesToBottom = () => {
        if (first) { first = false; throw new Error('Setup failed'); }
    };
    await w.submit();
    assert.equal(w.c.generating, false);
    assert.equal(w.c.promptInput.disabled, false);
    assert.equal(w.c.sendButton.classList.contains('loading'), false);
    assert.equal(w.c.status.textContent, 'Request failed');
});

test('explicit cancellation during chat retains the existing rollback behavior', async () => {
    const w = workspace({ chatPending: true });
    const task = w.submit();
    await flush();
    w.c.currentController.abort();
    await task;
    assert.equal(w.c.generating, false);
    assert.equal(w.c.promptInput.disabled, false);
    assert.equal(w.c.sendButton.classList.contains('loading'), false);
    assert.equal(w.c.promptInput.value, 'Build an indicator');
    assert.equal(w.c.history.length, 0);
    assert.equal(w.messageList[1].removed, true);
    assert.equal(w.c.status.textContent, 'Cancelled');
});

for (const heading of ['NinjaTrader Preflight Build', 'NinjaTrader Build Check', 'NinjaTrader Add-On Built']) {
    test(`${heading}: saved success remains eligible for download and renders current wording`, () => {
        const report = `# ${heading}\n\n## Build passed\n\nOld report wording`;
        const c = {
            history: [{ role: 'assistant', content: assistantText }, { role: 'assistant', content: report }],
            preflightRepairPromptPrefix: 'Repair the latest complete NinjaScript source so it passes',
            normalizePreflightRepairActions() {},
            document: {
                createElement() {
                    return Object.assign(element(), {
                        children: [], closest: () => null,
                        append(...children) { this.children.push(...children); },
                        appendChild(child) { this.children.push(child); }
                    });
                }
            }
        };
        vm.createContext(c);
        vm.runInContext([
            section('function isPreflightBuildReport(', 'function isBacktestReportSource('),
            section('function hasSuccessfulBuildForCode(', 'async function runPreflightBuild('),
            section('function countConsecutivePreflightRepairs(', 'function formatPreflightErrors('),
            section('function renderPreflightBuildReport(', 'async function downloadNinjaTraderAddon(')
        ].join('\n'), c);
        assert.equal(c.isPreflightBuildReport(c.history[1]), true);
        assert.equal(c.hasSuccessfulBuildForCode(code), true);
        c.history.unshift({ role: 'user', content: c.preflightRepairPromptPrefix + ' old repair' });
        assert.equal(c.countConsecutivePreflightRepairs(), 0);
        const container = c.document.createElement();
        c.renderPreflightBuildReport(container, report);
        assert.equal(container.children[0].children[0].textContent, 'NinjaTrader Add-On Built');
        assert.equal(container.children[0].children[1].textContent, 'Build passed');
        assert.equal(container.children[1].textContent,
            'Xen successfully compiled the source against the installed NinjaTrader assemblies. No build errors were found.');
        assert.equal(container.children[2].textContent,
            'The add-on is ready to download and install in NinjaTrader.');
        const failed = { role: 'assistant', content: '# NinjaTrader Add-On Build\n\n## Build failed' };
        c.history.push(failed);
        assert.equal(c.hasSuccessfulBuildForCode(code), false);
        assert.equal(c.compactPreflightBuildHistory(c.history).filter(c.isPreflightBuildReport).length, 1);
    });
}

test('new successful report stores the same final wording for every supported build task', () => {
    for (const task of ['build-indicator', 'build-strategy', 'existing-indicator',
        'existing-strategy', 'convert-indicator', 'convert-strategy']) {
        const w = workspace();
        w.c.activeTask = task;
        w.c.document = { createElement: () => ({}) };
        w.c.messages = { querySelectorAll: () => [] };
        const addMessage = w.c.addMessage;
        w.c.addMessage = (...args) => {
            const result = addMessage(...args);
            result.content.appendChild = () => {};
            return result;
        };
        w.c.normalizePreflightRepairActions = () => {};
        vm.runInContext([
            section('function isPreflightBuildReport(', 'function isBacktestReportSource('),
            section('function renderPreflightBuildResult(', 'function startPreflightRepair(')
        ].join('\n'), w.c);
        w.c.renderPreflightBuildResult({ success: true });
        assert.equal(w.c.history.at(-1).content,
            '# NinjaTrader Add-On Built\n\n## Build passed\n\n' +
            'Xen successfully compiled the source against the installed NinjaTrader assemblies. No build errors were found.\n\n' +
            'The add-on is ready to download and install in NinjaTrader.', task);
    }
});
