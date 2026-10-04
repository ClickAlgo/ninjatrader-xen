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

function modelSettings(saved = {}) {
    const html = fs.readFileSync(path.join(__dirname, '../wwwroot/workspace.html'), 'utf8')
        .split('<select id="modelSelect">')[1].split('</select>')[0].replace(/<!--[\s\S]*?-->/g, '');
    const options = [...html.matchAll(/<option value="([^"]+)"([^>]*)>/g)].map(([, value, attrs]) => ({
        value, hidden: /\bhidden\b/.test(attrs), disabled: /\bdisabled\b/.test(attrs),
        dataset: {
            modelGeneration: attrs.match(/data-model-generation="([^"]+)"/)?.[1],
            modelFamily: attrs.match(/data-model-family="([^"]+)"/)?.[1],
            fallbackModel: attrs.match(/data-fallback-model="([^"]+)"/)?.[1]
        },
        hasAttribute(name) { return attrs.includes(name); }
    }));
    const storage = new Map(Object.entries(saved));
    const c = { clearPreflightDiagnostics() {}, refreshPreflightControls() {}, renderExistingCodeAttachments() {}, isSourceAttachmentTask: () => false, currentCheckingSource: () => code,
        defaultModel: 'gpt-6-sol', modelGenerationStorageKey: 'nx_model_generation',
        legacyModelReplacements: new Map([['gpt-5.3-codex', 'gpt-6-sol'], ['claude-opus-5', 'claude-opus-5-5']]),
        localStorage: { getItem: key => storage.get(key), setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key) },
        modelSelect: { value: 'gpt-6-sol', options, querySelectorAll(selector) {
            return options.filter(o => o.hasAttribute(selector.slice(1, -1)));
        } }
    };
    vm.runInNewContext(section('function restoreSelectedModel()', 'function openSettings()'), c);
    c.applyPreferredModelGeneration();
    c.restoreSelectedModel();
    return { c, storage, visible: () => options.filter(o => !o.hidden && !o.disabled).map(o => o.value) };
}

test('generation defaults, visibility, family switching and reload stay synchronized', () => {
    for (const preference of [undefined, 'bad', 'established']) {
        const w = modelSettings(preference ? { nx_model_generation: preference } : {});
        assert.deepEqual(w.visible(), preference === 'established'
            ? ['gpt-6-sol', 'claude-opus-5-5', 'gpt-5.6-luna']
            : ['gpt-6.1-sol', 'claude-opus-5-5', 'gpt-6-luna']);
        w.c.modelSelect.value = 'gpt-5.6-luna';
        w.c.setModelGeneration('latest', true);
        assert.equal(w.c.modelSelect.value, 'gpt-6-luna');
        assert.deepEqual(w.visible(), ['gpt-6.1-sol', 'claude-opus-5-5', 'gpt-6-luna']);
        w.storage.set('nx_selected_model', w.c.modelSelect.value);
        assert.equal(modelSettings(Object.fromEntries(w.storage)).c.modelSelect.value, 'gpt-6-luna');
        w.c.setModelGeneration('established', true);
        assert.equal(w.c.modelSelect.value, 'gpt-5.6-luna');
        w.c.modelSelect.value = 'gpt-6-sol';
        w.c.setModelGeneration('latest', true);
        assert.equal(w.c.modelSelect.value, 'gpt-6.1-sol');
        w.c.modelSelect.value = 'claude-opus-5-5';
        w.c.setModelGeneration('established', true);
        assert.equal(w.c.modelSelect.value, 'claude-opus-5-5');
    }
});

test('valid saved versions survive reload and retired Codex resolves specifically to Sol 6', () => {
    for (const model of ['gpt-6-sol', 'gpt-6.1-sol', 'gpt-5.6-luna', 'gpt-6-luna', 'claude-opus-5-5', 'gpt-5.6-sol', 'claude-sonnet-4-6']) {
        const w = modelSettings({ nx_selected_model: model, nx_model_generation: 'latest' });
        assert.equal(w.c.modelSelect.value, model);
        assert.ok(w.visible().includes(model));
    }
    const w = modelSettings({ nx_selected_model: 'gpt-5.3-codex', nx_model_generation: 'latest' });
    assert.equal(w.c.modelSelect.value, 'gpt-6-sol');
    assert.equal(w.storage.get('nx_selected_model'), 'gpt-6-sol');
    assert.equal(w.storage.get('nx_model_generation'), 'established');
});

test('compiler repair never changes selected model, including when an obsolete override is passed', () => {
    for (const model of ['gpt-6-sol', 'gpt-6.1-sol', 'gpt-5.6-luna', 'gpt-6-luna', 'claude-opus-5-5']) {
        let submitted = false;
        const c = { clearPreflightDiagnostics() {}, refreshPreflightControls() {}, renderExistingCodeAttachments() {}, isSourceAttachmentTask: () => false, currentCheckingSource: () => 'uploaded source', generating: false, modelSelect: { value: model }, promptInput: {}, status: {},
            preflightRepairPromptPrefix: 'Repair', formatPreflightErrors: () => 'CS1000', updateClearInputButton() {},
            form: { requestSubmit() { submitted = true; } } };
        vm.runInNewContext(section('function startPreflightRepair(', 'function countConsecutivePreflightRepairs('), c);
        c.startPreflightRepair([], 'gpt-5.3-codex');
        assert.equal(c.modelSelect.value, model);
        assert.ok(submitted);
    }
});

test('reopening projects synchronizes generation without rewriting the saved project model', () => {
    for (const model of ['gpt-6-sol', 'gpt-6.1-sol', 'gpt-5.6-luna', 'gpt-6-luna', 'claude-opus-5-5', 'gpt-5.3-codex']) {
        const w = modelSettings({ nx_model_generation: 'latest' });
        w.c.project = { model };
        w.c.rememberSelectedModel = () => w.storage.set('nx_selected_model', w.c.modelSelect.value);
        w.c.updateModelCostBadge = w.c.updateImageUploadUi = () => {};
        vm.runInNewContext(section('    const projectModel = replaceLegacyModel(project.model);', '    messages.innerHTML = "";'), w.c);
        const expected = model === 'gpt-5.3-codex' ? 'gpt-6-sol' : model;
        assert.equal(w.c.modelSelect.value, expected);
        assert.ok(w.visible().includes(expected));
        assert.equal(w.c.project.model, model);
        assert.equal(modelSettings(Object.fromEntries(w.storage)).c.modelSelect.value, expected);
    }
});

for (const model of ['gpt-6-sol', 'gpt-6.1-sol', 'gpt-5.6-luna', 'gpt-6-luna', 'claude-opus-5-5']) {
    test('repair chat and newly saved project use selected model: ' + model, async () => {
        const w = workspace({ repair: true, model });
        await w.submit();
        for (const url of ['/api/chat/stream', '/api/projects']) {
            const request = w.requests.find(r => r.url === url);
            assert.ok(request);
            assert.equal(request.body.model, model);
        }
    });
}

for (const options of [
    { incomplete: true },
    { incomplete: true, repair: true },
    { noDone: true },
    { noDone: true, readError: true },
    { text: '### Overview\n\n```csharp\npublic class RV : Indicator { void OnStateChange() {} private static DateTime EasterSun' }
]) {
    test('incomplete response is not saved or built: ' + JSON.stringify(options), async () => {
        const w = workspace(options);
        await w.submit();
        assert.equal(w.requests.filter(r => r.url === '/api/preflight/build').length, 0);
        assert.equal(w.requests.filter(r => r.url === '/api/projects').length, 0);
        assert.match(w.c.status.textContent, /last complete code kept/);
        assert.equal(w.c.generating, false);
    });
}

test('incomplete initial build does not create a project', async () => {
    const w = workspace({ incomplete: true });
    w.c.currentProjectPersisted = false;
    await w.submit();
    assert.equal(w.requests.filter(r => r.url === '/api/projects').length, 0);
    assert.match(w.c.status.textContent, /last complete code kept/);
});

test('unfinished fenced code stays in a labelled code box with copy but no build actions', () => {
    const blocks = [];
    let actions = 0;
    const context = {
        activeTask: 'build-indicator', appendIncompleteRecoveryAction() {},
        appendProse() {}, decorateRequirementsMatch() {},
        appendCodeBlock(...args) { blocks.push(args); },
        appendResponseCodeActions() { actions++; }
    };
    vm.runInNewContext([
        section('function renderStructuredResponse(', 'function renderBacktestReport('),
        section('function looksLikeCompleteNinjaScript(', 'function openExistingCodeModal('),
        section('function responseHasIncompleteCode(', 'function isGeneratedRepairPrompt(')
    ].join('\n'), context);
    const partial = 'public class RV : Indicator { void OnStateChange() {} private static DateTime EasterSun';
    context.renderStructuredResponse({}, '### Overview\n\n```csharp\n' + partial);
    assert.equal(blocks.length, 1);
    assert.equal(blocks[0][1], partial);
    assert.match(blocks[0][2], /Incomplete/);
    assert.equal(blocks[0][3], false); // Copy-only toolbar
    assert.equal(actions, 0);
    assert.equal(context.extractLatestCodeBlock('```csharp\n' + partial + '\n```'), '');
});

test('balanced complete source accepts braces in comments and verbatim strings', () => {
    const context = {};
    vm.runInNewContext([
        section('function looksLikeCompleteNinjaScript(', 'function openExistingCodeModal('),
        section('function responseHasIncompleteCode(', 'function isGeneratedRepairPrompt(')
    ].join('\n'), context);
    const valid = 'public class RV : Indicator { void OnStateChange() { var path = @"C:\\"; /* } */ } }';
    assert.equal(context.looksLikeCompleteNinjaScript(valid), true);
    assert.equal(context.extractLatestCodeBlock('```csharp\n' + valid + '\n```'), valid);
    assert.equal(context.extractLatestCodeBlock('```csharp\n' + valid.slice(0, -1) + '\n```'), '');
});

test('large complete fenced source remains extractable and build eligible', () => {
    const context = {};
    vm.runInNewContext([
        section('function looksLikeCompleteNinjaScript(', 'function openExistingCodeModal('),
        section('function responseHasIncompleteCode(', 'function isGeneratedRepairPrompt(')
    ].join('\n'), context);
    const methods = Array.from({ length: 2000 }, (_, index) =>
        `private double Value${index}() { return ${index}; }`).join('\n');
    const largeCode = `namespace NinjaTrader.NinjaScript.Indicators {
public class LargeIndicator : Indicator {
protected override void OnStateChange() {}
${methods}
}
}`;
    assert.ok(largeCode.length > 70000);
    assert.equal(context.responseHasIncompleteCode('```csharp\n' + largeCode + '\n```'), false);
    assert.equal(context.extractLatestCodeBlock('```csharp\n' + largeCode + '\n```'), largeCode);
});

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
        section('function responseHasIncompleteCode', 'function isGeneratedRepairPrompt'),
        context);

    assert.equal(context.extractLatestCodeBlock(
        'Here is the change: class RelativeVolume : Indicator { void OnStateChange() {} }'), '');
    assert.equal(context.extractLatestCodeBlock(
        'public class RelativeVolume : Indicator { void OnStateChange() { }'), '');
});

test('conversion project titles prefer the declared source name', () => {
    const context = { activeTask: 'convert-strategy' };
    vm.runInNewContext(
        section('function createProjectTitle(', 'function updateProjectTitle('),
        context);

    assert.equal(context.createConversionProjectTitle(
        'Strategy.txt', 'strategy("EMA Intrabar Timing Test", overlay=true)'),
        'EMA Intrabar Timing Test');
    assert.equal(context.createConversionProjectTitle(
        'uploaded-system.mq5', 'void OnTick() {}'),
        'uploaded-system');
    assert.equal(context.createConversionProjectTitle(
        'Strategy.txt', 'public class FastCrossStrategy : Strategy {}'),
        'Fast Cross Strategy');
    assert.equal(context.createConversionProjectTitle(
        'Strategy.txt', '[Robot] public class LondonBreakoutBot : Robot {}'),
        'London Breakout Bot');
    assert.equal(context.createConversionProjectTitle(
        'Strategy.txt', '#property description "Session Breakout EA"\nvoid OnTick() {}'),
        'Session Breakout EA');
    assert.equal(context.createConversionProjectTitle(
        'Strategy.txt', '//| MomentumTrader.mq5 |\nvoid OnTick() {}'),
        'MomentumTrader');
    assert.equal(context.createConversionProjectTitle(
        'Strategy.txt', 'unknown source language'),
        'Convert Strategy');
});

test('converted strategy download warning can be acknowledged and hidden', async () => {
    const stored = new Map();
    const classes = new Set();
    const context = {
        activeTask: 'convert-strategy',
        strategyConversionRiskHiddenKey: 'risk-hidden',
        strategyConversionRiskModal: { hidden: true },
        hideStrategyConversionRisk: { checked: false },
        resolveStrategyConversionRisk: null,
        localStorage: {
            getItem: key => stored.get(key) ?? null,
            setItem: (key, value) => stored.set(key, value)
        },
        document: { body: { classList: {
            add: name => classes.add(name),
            remove: name => classes.delete(name)
        } } }
    };
    vm.runInNewContext(
        section('function confirmStrategyConversionDownload(', 'function appendPreflightRepairActions('),
        context);

    const decision = context.confirmStrategyConversionDownload();
    assert.equal(context.strategyConversionRiskModal.hidden, false);
    context.hideStrategyConversionRisk.checked = true;
    context.closeStrategyConversionRisk(true);
    assert.equal(await decision, true);
    assert.equal(stored.get('risk-hidden'), 'true');
    assert.equal(context.strategyConversionRiskModal.hidden, true);

    assert.equal(await context.confirmStrategyConversionDownload(), true);
    assert.equal(context.strategyConversionRiskModal.hidden, true);
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
    const c = { clearPreflightDiagnostics() {}, refreshPreflightControls() {}, renderExistingCodeAttachments() {}, isSourceAttachmentTask: () => false, currentCheckingSource: () => code,
        AbortController, TextDecoder, console,
        generating: false, preparingRequest: false, preflightBuilding: false,
        currentController: null, currentBalanceGbp: options.balance ?? 1,
        currentProjectId: 'project-1', currentProjectTitle: 'Example',
        currentProjectPersisted: true, hasProjectSnapshots: false,
        activeTask: options.task ?? 'build-indicator', history: [], pendingImage: null,
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
        isExistingCodeTask: () => String(options.task ?? '').startsWith('existing-'),
        isSourceAttachmentTask: () => /^(?:existing|convert)-/.test(options.task ?? ''),
        hasCurrentExistingSource: () => options.hasSource === true,
        redirectMismatchedExistingSource: () => false,
        isClearlyFrustrated: () => false,
        reviewBuildPrompt: async prompt => prompt,
        isGeneratedRepairPrompt: () => options.repair === true,
        buildRetrievalPrompt: prompt => prompt,
        extractLatestCodeBlock: text => text.startsWith('> Xen: Response incomplete') ? '' : code,
        looksLikeCompleteNinjaScript: value => value === code,
        markBuildPlanResponseReady: () => options.automatic !== false,
        getTaskSwitchTargetFromResponse: () => null,
        isNewCodeBuildTask: () => /^(?:build-strategy|build-indicator)$/.test(options.task ?? 'build-indicator'),
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
            requests.push({ url, signal: init.signal, body: JSON.parse(init.body) });
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
                        if (sent) {
                            if (options.readError) throw new Error('connection lost');
                            return { done: true };
                        }
                        sent = true;
                        return { done: false, value: Buffer.from('data: ' + JSON.stringify({
                            type: 'response.output_text.delta', delta: options.text ?? assistantText
                        }) + '\n\n' + (options.incomplete ? 'data: ' + JSON.stringify({
                            type: 'response.incomplete', message: 'Output limit reached.'
                        }) + '\n\n' : '') + (options.noDone ? '' : 'data: [DONE]\n\n')) };
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
    for (const name of ['sendButton', 'cancelButton', 'modelSelect', 'promptInput', 'status', 'buildProgress'])
        c[name] = element();
    c.promptInput.value = options.emptyPrompt ? '' : 'Build an indicator';
    c.buildProgress.hidden = true;
    c.modelSelect.value = options.model || 'gpt-6-sol';
    for (const name of ['markBuildPlanPromptSent', 'clearPendingImage', 'clearAnalyzerExports',
        'updateClearInputButton', 'updateImageUploadUi', 'scrollMessagesToBottom',
        'addModelFeedbackControls', 'renderExistingCodeAttachments', 'updateHistoryButton',
        'restoreBuildPlanPromptLoaded']) c[name] = () => {};
    vm.createContext(c);
    vm.runInContext([
        section('function sourceReviewType(', 'function renderStructuredResponse('),
        section('function responseHasIncompleteCode(', 'function extractLatestCodeBlock('),
        section('function hasBalancedCodeBraces(', 'function isGeneratedRepairPrompt('),
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
    assert.equal(w.c.buildProgress.hidden, true);
    assert.equal(w.c.promptInput.disabled, disabled);
    assert.equal(w.c.sendButton.classList.contains('loading'), false);
    assert.equal(w.c.currentController, null);
    assert.equal(w.messageList[1].button.classList.contains('is-checking'), false);
    assert.equal(w.messageList[1].content.textContent, assistantText);
    assert.equal(w.messageList[1].removed, false);
    assert.equal(w.timers.size, 0);
}

for (const [task, expectedPrompt] of [
    ['existing-strategy', 'Review the attached NinjaTrader strategy source.'],
    ['existing-indicator', 'Review the attached NinjaTrader indicator source.'],
    ['convert-strategy', 'Convert the attached strategy to NinjaTrader 8.'],
    ['convert-indicator', 'Convert the attached indicator to NinjaTrader 8.']
]) {
    test(`${task}: attached source submits without additional information`, async () => {
        const w = workspace({ task, hasSource: true, emptyPrompt: true, automatic: false });
        await w.submit();
        const chatRequest = w.requests.find(request => request.url === '/api/chat/stream');
        assert.ok(chatRequest);
        assert.equal(chatRequest.body.prompt, expectedPrompt);
        assert.equal(w.c.history[0].content, expectedPrompt);
    });
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
        assert.equal(w.c.buildProgress.hidden, false);
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
        const c = { clearPreflightDiagnostics() {}, refreshPreflightControls() {}, renderExistingCodeAttachments() {}, isSourceAttachmentTask: () => false, currentCheckingSource: () => code,
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
            'The add-on is ready to download and install in NinjaTrader. Compile and test there to verify local dependencies and runtime behaviour.');
        const failed = { role: 'assistant', content: '# NinjaTrader Add-On Build\n\n## Build failed' };
        c.history.push(failed);
        assert.equal(c.hasSuccessfulBuildForCode(code), false);
        assert.equal(c.compactPreflightBuildHistory(c.history).filter(c.isPreflightBuildReport).length, 1);
    });
}

test('new successful report keeps the same compile-only wording for every supported build task', () => {
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
        w.c.normalizePreflightRepairActions = () => {}; w.c.refreshPreflightControls = () => {};
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
