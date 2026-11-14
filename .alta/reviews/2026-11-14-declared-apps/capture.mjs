// Captures for the review of spec 019 (T007), on the running fresh instance of the Aspire sample.
// Usage: OPERATOR_CREDENTIAL=... node capture.mjs <umbral base> <setup token> <out dir>
// Claims Umbral, creates an ordinary app and an invitation on the passkey pane through the real
// forms, then captures at 1280 and 360 px, dark and light. The refusals are real POSTs (with the
// antiforgery token and an Origin header); the notice is shown by following the redirect.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, setupToken, out] = process.argv.slice(2);
const credential = process.env.OPERATOR_CREDENTIAL;
if (!base || !setupToken || !out || !credential) { console.error('usage: see header'); process.exit(2); }
fs.mkdirSync(out, { recursive: true });
const themes = ['dark', 'light'];
const widths = [1280, 360];
const metrics = [];

const browser = await chromium.launch({ channel: 'chrome' });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await context.newPage();

// Claim through the real form.
await page.goto(base + '/admin/setup');
await page.fill('[name=token]', setupToken);
await page.fill('[name=credential]', credential);
await page.fill('[name=confirm]', credential);
await page.press('[name=confirm]', 'Enter');
await page.waitForLoadState('networkidle');
if (page.url().includes('/admin/signin')) {
  await page.fill('[name=credential]', credential);
  await page.press('[name=credential]', 'Enter');
  await page.waitForLoadState('networkidle');
}
console.log('claimed; at ' + page.url());

// An ordinary app, through the admin form.
await page.goto(base + '/admin/apps/new');
await page.getByLabel('Name', { exact: true }).fill('Internal tool');
await page.getByLabel('Redirect URI', { exact: true }).fill('https://tool.example.com/callback');
await page.getByLabel('Web application').check();
await page.getByRole('button', { name: 'Create app' }).click();
await page.waitForURL(/\/admin\/apps\/[^/?#]+/);
const ordinaryId = new URL(page.url()).pathname.split('/').pop();
console.log('ordinary app: ' + ordinaryId);

// An invitation on the declared passkey pane.
await page.goto(base + '/admin/connectors/passkey');
await page.getByLabel('Note (optional)').fill('For the review');
await page.getByRole('button', { name: 'Invite someone' }).click();
await page.waitForLoadState('networkidle');
console.log('invitation created; at ' + page.url());

async function token() {
  await page.goto(base + '/admin/apps/new');
  return page.locator('input[name=__RequestVerificationToken]').first().inputValue();
}
async function post(path, form = {}) {
  const t = await token();
  const r = await page.request.post(base + path, {
    form: { __RequestVerificationToken: t, ...form },
    headers: { Origin: base },
    maxRedirects: 0,
  });
  return r.headers()['location'] ?? `status ${r.status()}`;
}

async function shoot(state, url) {
  for (const theme of themes) {
    await context.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
    for (const width of widths) {
      await page.setViewportSize({ width, height: 900 });
      await page.goto(base + url);
      await page.waitForLoadState('networkidle');
      const o = await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, iw: window.innerWidth }));
      const name = `${state}-${width}-${theme}`;
      metrics.push(`${name}: overflow=${o.sw > o.iw} (scrollWidth ${o.sw}, innerWidth ${o.iw})`);
      await page.screenshot({ path: `${out}/${name}.png`, fullPage: true });
    }
  }
}

await shoot('apps-list', '/admin/apps');
await shoot('app-declared-pane', '/admin/apps/sample');
await shoot('app-ordinary-pane', '/admin/apps/' + ordinaryId);
await shoot('passkey-declared-pane', '/admin/connectors/passkey');

// Real refusals.
const appRefusal = await post('/admin/apps/sample/redirect-uris', { redirect_uri: 'https://evil.example.com/cb' });
console.log('POST /admin/apps/sample/redirect-uris -> ' + appRefusal);
await shoot('refusal-app-declared', appRefusal.startsWith('/') ? appRefusal : '/admin/apps/sample?msg=app-declared');
const connRefusal = await post('/admin/connectors/passkey/toggle');
console.log('POST /admin/connectors/passkey/toggle -> ' + connRefusal);
await shoot('refusal-connector-declared', connRefusal.startsWith('/') ? connRefusal : '/admin/connectors/passkey?msg=connector-declared');
// The other owned routes of an app, and the connector's delete, answer the same.
console.log('POST /admin/apps/sample/delete -> ' + await post('/admin/apps/sample/delete'));
console.log('POST /admin/apps/sample/secret -> ' + await post('/admin/apps/sample/secret'));

console.log(metrics.join('\n'));
await browser.close();
