const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname, '../wwwroot/js/workspace.js'), 'utf8');
function section(start, end) {
    return script.slice(script.indexOf(start), script.indexOf(end, script.indexOf(start)));
}
function element() {
    return { children: [], classList: { add() {} }, prepend(child) { this.children.unshift(child); },
        appendChild(child) { this.children.push(child); }, addEventListener() {},
        querySelectorAll() { return []; } };
}

for (const type of ['strategy', 'indicator']) {
    for (const restored of [true, false]) test(`${type} review has report title and print action (${restored ? 'restored' : 'new'})`, () => {
        const context = { document: { createElement: element },
            responseHasIncompleteCode: () => false, extractUnfencedNinjaScript: () => null,
            appendProse() {}, decorateRequirementsMatch() {} };
        vm.createContext(context);
        vm.runInContext(section('function sourceReviewType(', 'function renderBacktestReport('), context);
        const container = element();
        const reviewType = context.sourceReviewType(`Review the attached NinjaTrader ${type} source.`);
        context.renderStructuredResponse(container,
            restored ? '### Overview\nSaved review' : `# ${type === 'strategy' ? 'Strategy' : 'Indicator'} Review\n## Findings\nNo issues`,
            reviewType);
        assert.equal(container.children.at(-1).children[0].textContent, 'Print / Save PDF');
        if (restored) assert.equal(container.children[0].textContent, type === 'strategy' ? 'Strategy Review' : 'Indicator Review');
        assert.equal(context.sourceReviewType('Repair the attached source.'), null);
    });
}
for (const [score, repairExpected] of [['100%', false], ['99.5%', true], ['0%', true], ['unknown', false]]) {
    test(`requirements repair visibility for ${score}`, () => {
        const content = element();
        const context = { document: { createElement: element }, history: [],
            addMessage() { return { classList: { add() {} }, querySelector() { return content; } }; },
            modelDisplayName: x => x, renderStructuredResponse() {}, scrollMessagesToBottom() {} };
        vm.createContext(context);
        vm.runInContext(section('function requirementsMatchPercentage(', 'function printRequirementsReport(') +
            section('function renderRequirementsValidationResult(', 'function selectedModelDisplayName('), context);
        context.renderRequirementsValidationResult(`## Overall Match\n${score} — Result`, 'requirements', 'model');
        assert.equal(content.children.some(x => x.className === 'requirements-validation-actions'), repairExpected);
    });
}
test('print action is present even when a restored report has no score', () => {
    const context = { document: { createElement: element } };
    vm.createContext(context);
    vm.runInContext(section('function requirementsMatchPercentage(', 'function printRequirementsReport(') +
        section('function decorateRequirementsMatch(', 'function renderUserMessage('), context);
    const container = element();
    context.decorateRequirementsMatch(container, '# Requirements Verification\nNo score');
    assert.equal(container.children[0].children[0].textContent, 'Print / Save PDF');
});
