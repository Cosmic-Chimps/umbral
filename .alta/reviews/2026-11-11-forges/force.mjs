// Forced "make primary" on an unactivated address, outside the UI (confirmation on).
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
const [base, label] = process.argv.slice(2);
const browser = await chromium.launch({ channel: 'chrome' });
const page = await browser.newPage();
await page.goto(base + '/user/login'); await page.fill('#user_name', 'ana'); await page.fill('#password', 'Correct-Horse-Battery-1'); await page.click('form[action="/user/login"] button.primary'); await page.waitForLoadState('load');
await page.goto(base + '/user/settings/account');
const emails = () => page.locator('.flex-item, .ui.list .item').filter({ hasText: '@' }).allInnerTexts().then(a => a.map(s => s.replace(/\s+/g, ' ').trim()));
let csrf = await page.evaluate(() => window.config?.csrfToken ?? null);
if (!csrf) { const m = (await page.content()).match(/csrfToken\s*[:=]\s*['"]([^'"]+)['"]/) || (await page.content()).match(/name="_csrf"[^>]*value="([^"]+)"/); csrf = m && m[1]; }
console.log(label, 'csrf found:', !!csrf);
const forms = await page.locator('form[action="/user/settings/account/email"]').evaluateAll(fs => fs.map(f => Object.fromEntries([...f.querySelectorAll('input')].map(i => [i.name, i.value]))));
console.log(label, 'forms:', JSON.stringify(forms));
const ids = [...new Set(forms.map(f => f.id).filter(Boolean))];
for (const id of ids) {
  const r = await page.request.post(base + '/user/settings/account/email', { form: { _csrf: csrf, _method: 'PRIMARY', id }, maxRedirects: 0 });
  await page.goto(base + '/user/settings/account');
  console.log(label, 'forced PRIMARY id', id, '-> status', r.status(), 'flash:', JSON.stringify((await page.locator('.flash-message, .ui.message').allInnerTexts()).map(s => s.replace(/\s+/g, ' ').trim()).filter(s => !s.includes('permanently delete'))), 'emails:', JSON.stringify(await emails()));
}
await browser.close();
