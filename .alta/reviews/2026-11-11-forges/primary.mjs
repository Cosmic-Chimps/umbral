// An active account adds a second address while confirmation is on (benign part of the B1 re-check).
// The step that showed Forgejo accept an unconfirmed address as primary is held outside this public
// repository on the documentation reviewer's advice (B3); see notes.md.
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
await browser.close();
