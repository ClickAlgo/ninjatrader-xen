const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = fs.readFileSync(path.join(__dirname, '../wwwroot/js/workspace.js'), 'utf8');
const start = script.indexOf('function updateProjectSizeWarning()');
const end = script.indexOf('// Bound the entire operation', start);

test('size warning follows source growth, recovery and task changes', () => {
    const warning = {};
    const c = {
        document: { getElementById: () => warning },
        activeTask: 'build-indicator',
        existingCodeState: { sources: [], workingCode: null },
        source: '',
        getLatestGeneratedCode() { return this.source; },
        isExistingCodeTask() { return this.activeTask.startsWith('existing-'); }
    };
    // Bind callbacks explicitly rather than depending on VM global receiver semantics.
    c.getLatestGeneratedCode = () => c.source;
    c.isExistingCodeTask = () => c.activeTask.startsWith('existing-');
    vm.createContext(c);
    vm.runInContext(script.slice(start, end), c);
    for (const [length, hidden] of [[20346, true], [23999, true], [24000, false], [30000, false], [30001, false]]) {
        c.source = 'x'.repeat(length);
        c.updateProjectSizeWarning();
        assert.equal(warning.hidden, hidden);
        assert.match(warning.textContent, length > 30000 ? /exceeds/ : /approaching/);
    }
    c.source = 'x'.repeat(20000); // A restored complete snapshot clears the warning.
    c.updateProjectSizeWarning();
    assert.equal(warning.hidden, true);
    c.activeTask = 'existing-strategy';
    c.existingCodeState.workingCode = 'x'.repeat(25000);
    c.updateProjectSizeWarning();
    assert.equal(warning.hidden, false);
    c.activeTask = 'analyse-backtest';
    c.updateProjectSizeWarning();
    assert.equal(warning.hidden, true);
});
