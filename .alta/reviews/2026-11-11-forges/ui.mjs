// Read the registration pages of each forge as an operator sees them: headings, labels, checkboxes.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
const browser = await chromium.launch({ channel: 'chrome' });
async function dump(label, page, url) {
  await page.goto(url); await page.waitForLoadState('load');
  const h = await page.locator('h1, h2, h3, h4').allInnerTexts();
  const labels = await page.locator('label').allInnerTexts();
  const crumbs = await page.locator('nav a, aside a, .menu a, [data-testid*=nav] a').allInnerTexts();
  console.log(label, url, '\n  headings:', h.map(s => s.trim()).filter(Boolean).slice(0, 12).join(' | '), '\n  labels:', labels.map(s => s.trim().replace(/\s+/g, ' ')).filter(Boolean).slice(0, 20).join(' | '), '\n  nav:', [...new Set(crumbs.map(s => s.trim().replace(/\s+/g, ' ')).filter(s => s && s.length < 40))].slice(0, 60).join(' | '));
}
for (const [label, base] of [['FORGEJO', 'http://localhost:3001'], ['GITEA', 'http://localhost:3002']]) {
  const page = await browser.newPage();
  await page.goto(base + '/user/login'); await page.fill('#user_name', 'ana'); await page.fill('#password', 'Correct-Horse-Battery-1'); await page.click('form[action="/user/login"] button.primary'); await page.waitForLoadState('load');
  await dump(label + ' user settings', page, base + '/user/settings/applications');
  await dump(label + ' admin', page, base + '/-/admin/applications');
  await page.close();
}
{
  const page = await browser.newPage();
  await page.goto('http://localhost:3003/users/sign_in'); await page.fill('#user_login', 'root'); await page.fill('#user_password', 'Correct-Horse-Battery-1'); await page.click('[data-testid="sign-in-button"], button[type=submit]'); await page.waitForLoadState('load');
  await dump('GITLAB user settings', page, 'http://localhost:3003/-/user_settings/applications');
  await dump('GITLAB admin', page, 'http://localhost:3003/admin/applications/new');
  await page.close();
}
await browser.close();
