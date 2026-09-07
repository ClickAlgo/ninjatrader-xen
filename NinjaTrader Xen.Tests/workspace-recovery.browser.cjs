// Run against the local app: XEN_TEST_URL=http://127.0.0.1:5298 node this-file
// Requires Playwright. All API requests are intercepted; no live credentials or database.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');

(async () => {
    const base = process.env.XEN_TEST_URL || 'http://127.0.0.1:5298';
    const browser = await chromium.launch({ headless: true, channel: process.env.XEN_TEST_BROWSER || 'msedge' });
    try {
        const context = await browser.newContext({
            viewport: { width: 1440, height: 1000 },
            permissions: ['clipboard-read', 'clipboard-write']
        });
        const id = '00000000-0000-0000-0000-000000000123';
        const oldCode = 'namespace NinjaTrader.NinjaScript.Indicators { public class RV : Indicator { protected override void OnStateChange() {} } }';
        const newCode = 'namespace NinjaTrader.NinjaScript.Indicators { public class RV : Indicator { protected override void OnStateChange() {} private bool ExcludeHolidaySessions = true; } }';
        const partial = '> Xen: Response incomplete. Output limit reached.\n\n```csharp\n' +
            'namespace NinjaTrader.NinjaScript.Indicators { public class RV : Indicator { protected override void OnStateChange() {} private static DateTime EasterSun';
        let project = {
            projectId: id, title: 'Recovery browser fixture', task: 'build-indicator',
            model: 'claude-opus-5', latestCode: oldCode,
            messages: [
                { role: 'user', content: 'Build a relative-volume indicator.' },
                { role: 'assistant', content: '```csharp\n' + oldCode + '\n```' },
                { role: 'user', content: 'Add holiday exclusions.' },
                { role: 'assistant', content: partial }
            ]
        };
        let chatRequests = 0, restores = 0;
        const errors = [];
        await context.addInitScript(id => {
            sessionStorage.setItem('nx_access_token', 'browser-fixture-only');
            sessionStorage.setItem('nx_active_saved_project_id', id);
        }, id);
        const page = await context.newPage();
        page.on('pageerror', error => errors.push(error.message));
        await page.route('**/api/**', async route => {
            const request = route.request();
            const url = new URL(request.url());
            const json = body => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
            if (url.pathname === '/api/account/summary') return json({ balanceGbp: 20, subscriberId: 1 });
            if (url.pathname === '/api/projects/' + id) return json(project);
            if (url.pathname === '/api/projects/' + id + '/revisions' && request.method() === 'GET') {
                return json({ revisions: [{ revisionId: 7, versionNumber: 1, isCurrent: true }], totalCount: 1 });
            }
            if (url.pathname === '/api/projects/' + id + '/revisions/7/restore' && request.method() === 'POST') {
                restores++;
                project = {
                    ...project, latestCode: oldCode,
                    messages: project.messages.slice(0, 2)
                };
                return json(project);
            }
            if (url.pathname === '/api/chat/stream') {
                chatRequests++;
                const body = request.postDataJSON();
                assert.match(body.prompt, /^Repair the latest complete NinjaScript source after an incomplete response\./);
                assert.match(body.prompt, /Request to complete:\nAdd holiday exclusions\.$/);
                assert.equal(body.model, 'claude-opus-5');
                const response = chatRequests === 1
                    ? '```csharp\npublic class RV : Indicator { void OnStateChange() {}'
                    : '```csharp\n' + newCode + '\n```';
                const events = [{ type: 'response.output_text.delta', delta: response }];
                if (chatRequests === 1)
                    events.push({ type: 'response.incomplete', message: 'Output limit reached.' });
                events.push({ type: 'usage', balanceGbp: 19 });
                return route.fulfill({
                    status: 200, contentType: 'text/event-stream',
                    body: events.map(event => 'data: ' + JSON.stringify(event) + '\n\n').join('') + 'data: [DONE]\n\n'
                });
            }
            throw new Error('Unexpected API request: ' + request.method() + ' ' + url.pathname);
        });
        await page.goto(base + '/workspace.html');
        await page.getByRole('button', { name: 'Restore last snapshot', exact: true }).waitFor();
        assert.equal(chatRequests, 0);
        await page.locator('#promptInput').fill('Keep my unsent draft.');
        const recovery = page.getByRole('button', { name: 'Restore last snapshot', exact: true });
        await recovery.scrollIntoViewIfNeeded();
        const recoveryStyle = await recovery.evaluate(element => {
            const style = getComputedStyle(element);
            return {
                backgroundImage: style.backgroundImage,
                color: style.color,
                fontWeight: Number(style.fontWeight),
                width: element.getBoundingClientRect().width
            };
        });
        assert.match(recoveryStyle.backgroundImage, /linear-gradient/);
        assert.equal(recoveryStyle.color, 'rgb(255, 255, 255)');
        assert.ok(recoveryStyle.fontWeight >= 700);
        assert.ok(recoveryStyle.width >= 210);
        await recovery.click();
        assert.equal(restores, 0);
        assert.equal(await page.locator('#promptInput').inputValue(), 'Keep my unsent draft.');
        assert.match(await page.locator('#chatStatus').textContent(), /clear your current draft/);
        await page.locator('#promptInput').fill('');
        const output = process.env.XEN_TEST_ARTIFACTS ||
            path.join(require('node:os').tmpdir(), 'ninjatrader-xen-recovery-browser');
        fs.mkdirSync(output, { recursive: true });
        await page.screenshot({ path: path.join(output, 'incomplete-desktop.png') });
        await recovery.click();
        await page.waitForFunction(() => document.getElementById('chatStatus').textContent.includes('no AI credit used'));
        assert.equal(chatRequests, 0);
        assert.equal(restores, 1);
        assert.equal(await page.locator('#promptInput').inputValue(), '');
        assert.equal(project.latestCode, oldCode);
        assert.equal(project.messages.length, 2);
        await page.reload();
        await page.locator('.response-code-actions').last().waitFor();
        const codeActions = page.locator('.response-code-actions').filter({ has: page.getByRole('button', { name: 'Verify requirements', exact: true }) }).last();
        await codeActions.getByRole('button', { name: 'Copy', exact: true }).click();
        assert.equal(await page.evaluate(() => navigator.clipboard.readText()), oldCode);
        await page.setViewportSize({ width: 390, height: 844 });
        await page.screenshot({ path: path.join(output, 'recovered-mobile.png'), fullPage: true });
        assert.deepEqual(errors, []);
        console.log('PASS: last database snapshot restored without an AI request or credit, then persisted across reopen.');
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
