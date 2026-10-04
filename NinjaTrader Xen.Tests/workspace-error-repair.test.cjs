const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname, '../wwwroot/js/workspace.js'), 'utf8');
function section(start, end) { return script.slice(script.indexOf(start), script.indexOf(end, script.indexOf(start))); }

function workflow(existing = true) {
    const uploaded = 'namespace NinjaTrader.NinjaScript.Strategies { public class Uploaded : Strategy {} }';
    const corrected = uploaded.replace('Uploaded', 'Corrected');
    const check = { sourceCode: uploaded, after(action) { this.fix = action; } };
    const older = { sourceCode: 'old source' };
    let reportRemoved = false;
    const context = {
        history: [], generating: false, activeTask: 'existing-strategy', currentProjectId: 'same-project',
        modelSelect: { value: 'selected-model' }, promptInput: {}, status: {},
        preflightRepairPromptPrefix: 'Repair', existingCodeState: { sources: [{ role: 'current-source', code: uploaded }], workingCode: null },
        isExistingCodeTask: () => existing, getLatestGeneratedCode: () => context.latest || uploaded,
        updateClearInputButton() {}, form: { requestSubmit() { context.submitted = true; } },
        document: { createElement() { return { appendChild(child) { this.firstChild = child; }, addEventListener() {} }; } },
        messages: { querySelectorAll(selector) {
            if (selector === '.preflight-build-button') return [older, check];
            return [{ remove() { delete check.fix; reportRemoved = true; } }];
        } },
        existingCodeAttachments: { querySelector: () => check, querySelectorAll: () => [{ remove() { delete check.fix; } }] }
    };
    vm.createContext(context);
    vm.runInContext([
        section('function appendPreflightRepairActions(', 'function normalizePreflightRepairActions('),
        section('function parsePreflightDiagnostics(', 'function groupPreflightDiagnostics('),
        section('function isPreflightBuildReport(', 'function isBacktestReportSource('),
        section('function startPreflightRepair(', 'function countConsecutivePreflightRepairs('),
        section('function formatPreflightErrors(', 'function highlightCSharp('),
    ].join('\n'), context);
    return { context, check, older, uploaded, corrected, reportRemoved: () => reportRemoved };
}

for (const existing of [true, false]) test(`uploaded/current source → errors → repair → corrected source → stale diagnostics cleared (${existing})`, () => {
    const w = workflow(existing), c = w.context;
    c.history.push({ role: 'assistant', content: '# NinjaTrader Add-On Build\n\n## Build failed\n\n- CS1002 at line 1, column 2: Missing semicolon' });
    c.refreshPreflightControls();
    assert.equal(w.check.fix.firstChild.textContent, 'Fix errors');
    assert.equal(w.older.hidden, true);
    assert.equal(w.check.hidden, existing);
    c.startPreflightRepair(c.parsePreflightDiagnostics(c.history[0].content));
    assert.ok(c.submitted);
    assert.ok(c.promptInput.value.includes('CS1002'));
    assert.equal(c.currentCheckingSource(), w.uploaded);
    assert.equal(c.modelSelect.value, 'selected-model');
    assert.equal(c.currentProjectId, 'same-project');
    c.history.push({ role: 'assistant', content: '```csharp\n' + w.corrected + '\n```' });
    c.existingCodeState.workingCode = c.latest = w.corrected;
    c.clearPreflightDiagnostics();
    w.check.sourceCode = w.corrected;
    c.refreshPreflightControls();
    assert.equal(w.check.fix, undefined);
    assert.equal(c.history.filter(c.isPreflightBuildReport).length, 0);
    assert.equal(c.currentCheckingSource(), w.corrected);
    assert.ok(w.reportRemoved());
});

test('compiler infrastructure and mixed failures never offer automatic source repair', () => {
    for (const diagnostic of ['- TIMEOUT at build: Timed out', '- BUILD at build: No diagnostics',
        '- MSB1009 at line 1: Project missing', '- NU1301 at build: Restore failed',
        '- CS1002 at line 1: Syntax error\n- BUILD at build: Infrastructure failed']) {
        const w = workflow();
        w.context.history.push({ role: 'assistant', content: '# NinjaTrader Add-On Build\n\n## Build failed\n\n' + diagnostic });
        w.context.refreshPreflightControls();
        assert.equal(w.check.fix, undefined, diagnostic);
    }
    assert.equal(workflow().context.isRepairablePreflightDiagnostic({ code: 'NTX1001', line: 3 }), true);
});
