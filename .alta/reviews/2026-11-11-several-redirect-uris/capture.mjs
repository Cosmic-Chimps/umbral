// Capture walk for task r3du (spec 012). Args: base URL, setup token, output folder.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, setupToken, out] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const credential = 'a-very-good-credential-r3du';

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

async function post(path, form) {
  const t = await token();
  const r = await page.request.post(base + path, {
    form: { __RequestVerificationToken: t, ...form },
    headers: { Origin: base },
    maxRedirects: 0,
  });
  return r.headers()['location'] ?? `status ${r.status()}`;
}

// A connector, so the Apps screen can create apps.
console.log('passkey connector:', await post('/admin/connectors/passkey', { enrolment: 'open', shared: 'on' }));

async function createApp(name, redirectUri) {
  const loc = await post('/admin/apps', {
    name, redirectUri, postLogoutRedirectUri: '', appType: 'web', connectorChoice: 'all-shared',
  });
  console.log('create', name, loc);
  return decodeURIComponent(loc.split('?')[0].split('/').pop());
}

const one = await createApp('Grafana', 'https://grafana.example.com/login/generic_oauth');
const several = await createApp('Wiki', 'https://wiki.example.com/oauth/callback');
for (const uri of [
  'http://localhost:3000/oauth/callback',
  'https://staging.wiki.example.com/a/rather/long/path/that/must/wrap/on/a/narrow/screen/oauth/callback?tenant=engineering',
]) console.log(await post(`/admin/apps/${several}/redirect-uris`, { uri }));
for (const uri of ['https://wiki.example.com/signed-out', 'http://localhost:3000/signed-out'])
  console.log(await post(`/admin/apps/${several}/post-logout-uris`, { uri }));
const full = await createApp('Desktop tool', 'http://127.0.0.1:49152/callback');
for (let i = 1; i < 10; i++)
  console.log(await post(`/admin/apps/${full}/redirect-uris`, { uri: `http://127.0.0.1:${49152 + i}/callback` }));

const states = [
  ['create', () => '/admin/apps/new'],
  ['one-uri', () => `/admin/apps/${one}`],
  ['several', () => `/admin/apps/${several}`],
  ['added', () => `/admin/apps/${several}?msg=app-redirect-uri-added`],
  ['removed', () => `/admin/apps/${several}?msg=app-redirect-uri-removed`],
  ['refused-invalid', () => `/admin/apps/${several}?msg=app-redirect-uri-invalid`],
  ['refused-duplicate', () => `/admin/apps/${several}?msg=app-redirect-uri-duplicate`],
  ['refused-last', () => `/admin/apps/${one}?msg=app-redirect-uri-last`],
  ['full', () => `/admin/apps/${full}`],
  ['refused-limit', () => `/admin/apps/${full}?msg=app-redirect-uri-limit`],
];

// Check that the refusal codes are what the routes answer, not only drawn.
console.log('invalid ->', await post(`/admin/apps/${several}/redirect-uris`, { uri: 'https://*.example.com/cb' }));
console.log('duplicate ->', await post(`/admin/apps/${several}/redirect-uris`, { uri: 'https://WIKI.example.com/oauth/callback' }));
console.log('last ->', await post(`/admin/apps/${one}/redirect-uris/remove`, { uri: 'https://grafana.example.com/login/generic_oauth' }));
console.log('limit ->', await post(`/admin/apps/${full}/redirect-uris`, { uri: 'http://127.0.0.1:50000/callback' }));

const metrics = [];
for (const theme of ['dark', 'light']) {
  await context.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
  for (const width of [1280, 360]) {
    await page.setViewportSize({ width, height: 900 });
    for (const [name, path] of states) {
      await page.goto(base + path());
      await page.waitForLoadState('networkidle');
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      metrics.push(`${name}-${width}-${theme}: overflow=${overflow}`);
      await page.screenshot({ path: `${out}/${name}-${width}-${theme}.png`, fullPage: true });
    }
  }
}

// Keyboard focus on a Remove button, 1280 dark.
await context.addCookies([{ name: 'umbral_theme', value: 'dark', url: base }]);
await page.setViewportSize({ width: 1280, height: 900 });
await page.goto(base + `/admin/apps/${several}`);
const remove = page.locator('[data-redirect-uris] li button').first();
await remove.focus();
await page.keyboard.press('Shift+Tab');
await page.keyboard.press('Tab');
await remove.scrollIntoViewIfNeeded();
await page.screenshot({ path: `${out}/focus-remove-1280-dark.png` });

// The confirmation text (a native confirm(), not capturable): read it from the form.
const confirm = await page.locator('[data-redirect-uris] form[data-confirm]').first().getAttribute('data-confirm');
const confirmPl = await page.locator('[data-post-logout-uris] form[data-confirm]').first().getAttribute('data-confirm');
const aria = await remove.getAttribute('aria-label');
console.log('confirm redirect:', confirm);
console.log('confirm post-logout:', confirmPl);
console.log('remove aria-label:', aria);
console.log(metrics.join('\n'));

await browser.close();
