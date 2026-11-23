// An active account adds an address it never confirmed and tries to make it primary (confirmation on).
// node primary.mjs <base> <label>
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
const [base, label] = process.argv.slice(2);
const browser = await chromium.launch({ channel: 'chrome' });
const page = await browser.newPage();
await page.goto(base + '/user/login'); await page.fill('#user_name', 'ana'); await page.fill('#password', 'Correct-Horse-Battery-1'); await page.click('form[action="/user/login"] button.primary'); await page.waitForLoadState('load');
await page.goto(base + '/user/settings/account');
const emails = () => page.locator('.email, .flex-list .flex-item, .ui.list .item').filter({ hasText: '@' }).allInnerTexts().then(a => a.map(s => s.replace(/\s+/g, ' ').trim()));
console.log(label, 'emails before:', JSON.stringify(await emails()));
await page.fill('form[action="/user/settings/account/email"] input[name=email]', 'other@example.test');
await page.click('form[action="/user/settings/account/email"] button.primary');
await page.waitForLoadState('load');
console.log(label, 'after add:', JSON.stringify((await page.locator('.flash-message, .ui.message').allInnerTexts()).map(s => s.replace(/\s+/g, ' ').trim())));
console.log(label, 'emails after add:', JSON.stringify(await emails()));
const buttons = await page.locator('form[action="/user/settings/account/email"] button, .email button, .flex-item button').allInnerTexts();
console.log(label, 'buttons on the list:', JSON.stringify([...new Set(buttons.map(s => s.trim()).filter(Boolean))]));
// The id of the new address from the hidden inputs of the per-email forms, then a forced "make primary" post.
const ids = await page.locator('input[name=id]').evaluateAll(els => els.map(e => e.value));
const csrf = await page.locator('input[name=_csrf]').first().inputValue();
console.log(label, 'email ids:', JSON.stringify(ids));
for (const id of ids) {
  const r = await page.request.post(base + '/user/settings/account/email', { form: { _csrf: csrf, _method: 'PRIMARY', id }, maxRedirects: 0 });
  await page.goto(base + '/user/settings/account');
  console.log(label, 'forced PRIMARY on id', id, '-> status', r.status(), 'flash:', JSON.stringify((await page.locator('.flash-message, .ui.message').allInnerTexts()).map(s => s.replace(/\s+/g, ' ').trim())), 'emails now:', JSON.stringify(await emails()));
}
await browser.close();
