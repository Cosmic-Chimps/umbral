// Capture walk for the design review of task fl4t (discovery reasons in one level of brackets).
// Usage: node capture.mjs <base> <setup-token> <out>
//   One instance with the development exception (http://127.0.0.1:PORT), started with
//   HTTPS_PROXY=http://127.0.0.1:9 so that every https check (Microsoft's) is refused at once.
//   The OpenID Connect provider is added at http://127.0.0.1:9, refused the same way.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, setupToken, out] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const credential = 'a-very-good-credential-fl4t';
const themes = ['dark', 'light'];
const widths = [1280, 360];
const metrics = [];

const browser = await chromium.launch({ channel: 'chrome' });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await context.newPage();

// First-run setup through the real form.
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

async function token() {
  await page.goto(base + '/admin/apps');
  return page.locator('input[name=__RequestVerificationToken]').first().inputValue();
}
// A POST that does not follow the redirect, so the notice's one-shot reason cookie is
// kept in the context and read by the first GET of the location.
async function post(path, form) {
  const t = await token();
  const r = await page.request.post(base + path, {
    form: { __RequestVerificationToken: t, ...form },
    headers: { Origin: base },
    maxRedirects: 0,
  });
  return r.headers()['location'] ?? `status ${r.status()}`;
}
async function setTheme(theme) {
  await context.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
}
async function shot(name, width, theme) {
  await page.waitForLoadState('networkidle');
  const o = await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, iw: window.innerWidth }));
  metrics.push(`${name}-${width}-${theme}: overflow=${o.sw > o.iw} (scrollWidth ${o.sw}, innerWidth ${o.iw})`);
  await page.screenshot({ path: `${out}/${name}-${width}-${theme}.png`, fullPage: true });
}
const noticeText = () => page.locator('[data-notice]').first().innerText();

// The reason cookie is read once, so each shot posts again: the second save of the same
// connector gives the same notice for an unreachable provider.
async function noticeShots(name, save) {
  for (const theme of themes) {
    await setTheme(theme);
    for (const width of widths) {
      await page.setViewportSize({ width, height: 900 });
      const location = await save();
      await page.goto(new URL(location, base).toString());
      console.log(`${name} ${width} ${theme}: ${location}\n  ${await noticeText()}`);
      await shot(name, width, theme);
    }
  }
}

// 1. A generic OpenID Connect provider at a closed port: saved disabled. The first add
//    creates it; the later shots enable it, which gives the enable notice, so the add
//    notice is taken once per theme and width on a fresh slug each time.
let n = 0;
await noticeShots('oidc-added-unreachable', () =>
  post('/admin/connectors', {
    kind: 'oidc',
    slug: `home${++n}`,
    displayName: 'Home',
    issuer: `http://127.0.0.1:9/realm${n}`,
    clientId: 'umbral-client',
    clientSecret: 'not-a-real-secret-fl4t',
    shared: 'true',
  }));

// 2. Microsoft, personal accounts, behind the closed proxy: saved disabled.
await noticeShots('microsoft-saved-unreachable', () =>
  post('/admin/connectors/microsoft', {
    displayName: 'Microsoft',
    clientId: '3f1d7a52-9c4e-4b1a-8d2e-6a7b5c4d3e21',
    clientSecret: 'not-a-real-secret~0123456789',
    audience: 'consumers',
    shared: 'true',
  }));

console.log(metrics.join('\n'));
await browser.close();
