// Capture walk for task ap1e (spec 013, Sign in with Apple).
// Args: base URL, setup token, output folder, path of a P-256 private key in PEM (a throwaway one).
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, setupToken, out, keyPath] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const credential = 'a-very-good-credential-ap1e';
const pem = fs.readFileSync(keyPath, 'utf8');
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

async function shot(name, width, theme, fullPage = true) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  metrics.push(`${name}-${width}-${theme}: overflow=${overflow}`);
  await page.screenshot({ path: `${out}/${name}-${width}-${theme}.png`, fullPage });
}

// The Apple pane's form, filled and submitted in the page, so a refusal is the
// 422 page the operator sees. `fields` overrides the good values.
const good = { displayName: 'Apple', servicesId: 'com.example.signin', teamId: 'DEF123GHIJ', keyId: 'ABC123DEFG', privateKey: pem };
async function submitApple(fields) {
  await page.goto(base + '/admin/connectors/apple');
  const values = { ...good, ...fields };
  for (const [name, value] of Object.entries(values)) await page.fill(`[name=${name}]`, value);
  const save = page.locator('form[action="/admin/connectors/apple"] button[type=submit]').first();
  const [response] = await Promise.all([page.waitForResponse((r) => r.request().method() === 'POST'), save.click()]);
  await page.waitForLoadState('networkidle');
  return response.status();
}

// Another provider, so the picker shows Apple among others. Its credentials are made up.
console.log('github connector:', await post('/admin/connectors/github', {
  displayName: 'GitHub', clientId: 'Iv1.0123456789abcdef', clientSecret: 'not-a-real-secret-0123456789', shared: 'true', allowedDomains: '',
}));
console.log('passkey connector:', await post('/admin/connectors/passkey', { enrolment: 'open', shared: 'on' }));

// Phase A: no Apple connector yet.
for (const theme of themes) {
  await setTheme(theme);
  for (const width of widths) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(base + '/admin/connectors/apple');
    await page.waitForLoadState('networkidle');
    await shot('pane-not-set-up', width, theme);
    console.log('refused team id ->', await submitApple({ teamId: 'DEF123' }));
    await shot('refused-team-id', width, theme);
    console.log('refused key ->', await submitApple({ privateKey: 'this is not a key' }));
    await shot('refused-key', width, theme);
  }
}
// Several fields at once, 1280 dark only.
await setTheme('dark');
await page.setViewportSize({ width: 1280, height: 900 });
console.log('refused several ->', await submitApple({ servicesId: 'com example', teamId: 'x', keyId: 'ABC-123-DE', privateKey: '' }));
await shot('refused-several', 1280, 'dark');
console.log('key text on the refused page:', (await page.content()).includes('this is not a key'));

// Phase B: the real save. It signs with the throwaway key and fetches Apple's
// real discovery document; with no network it is saved disabled with the notice.
console.log('save ->', await submitApple({}), page.url());
const savedNotice = await page.locator('[role=status], .um-alert').allInnerTexts();
console.log('after save:', JSON.stringify(savedNotice));
for (const theme of themes) {
  await setTheme(theme);
  for (const width of widths) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(base + '/admin/connectors/apple');
    await page.waitForLoadState('networkidle');
    await shot('pane-set-up', width, theme);
    await page.goto(base + '/admin/connectors');
    await page.waitForLoadState('networkidle');
    await shot('list', width, theme);
  }
}
console.log('key in the set-up page:', (await page.content()).includes(pem.split('\n')[1]));

// Two apps: one plain, one with an accent colour.
async function createApp(name, redirectUri) {
  const loc = await post('/admin/apps', { name, redirectUri, postLogoutRedirectUri: '', appType: 'web', connectorChoice: 'all-shared' });
  console.log('create', name, loc);
  return decodeURIComponent(loc.split('?')[0].split('/').pop());
}
const plain = await createApp('Wiki', 'https://wiki.example.com/oauth/callback');
const branded = await createApp('Acme Notes', 'https://notes.example.com/oauth/callback');
console.log('accent:', await post(`/admin/apps/${branded}/branding/accent`, { accent: '#b4532a' }));

function authorize(clientId, redirectUri) {
  const q = new URLSearchParams({
    client_id: clientId, redirect_uri: redirectUri, response_type: 'code', scope: 'openid',
    state: 'capture', code_challenge: 'E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM', code_challenge_method: 'S256',
  });
  return `${base}/connect/authorize?${q}`;
}

const signedOut = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const visitor = await signedOut.newPage();
async function visitorShot(name, width, theme, url, lastUsed) {
  const cookies = [{ name: 'umbral_theme', value: theme, url: base }];
  await signedOut.clearCookies();
  if (lastUsed) cookies.push({ name: 'umbral_last_connector', value: lastUsed, url: base });
  await signedOut.addCookies(cookies);
  await visitor.setViewportSize({ width, height: 900 });
  await visitor.goto(url);
  await visitor.waitForLoadState('networkidle');
  const overflow = await visitor.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  metrics.push(`${name}-${width}-${theme}: overflow=${overflow}`);
  await visitor.screenshot({ path: `${out}/${name}-${width}-${theme}.png`, fullPage: true });
}

for (const theme of themes) {
  for (const width of widths) {
    await visitorShot('picker', width, theme, authorize(plain, 'https://wiki.example.com/oauth/callback'));
    await visitorShot('picker-accent', width, theme, authorize(branded, 'https://notes.example.com/oauth/callback'));
    await visitorShot('picker-last-used', width, theme, authorize(plain, 'https://wiki.example.com/oauth/callback'), 'apple');
  }
  // Keyboard focus on the Apple button.
  await visitorShot('picker-focus-scratch', 1280, theme, authorize(plain, 'https://wiki.example.com/oauth/callback'));
  const apple = visitor.locator('a[data-connector=apple]');
  for (let i = 0; i < 30; i++) {
    await visitor.keyboard.press('Tab');
    if (await apple.evaluate((el) => el === document.activeElement)) break;
  }
  await visitor.screenshot({ path: `${out}/picker-focus-apple-1280-${theme}.png`, fullPage: true });
  fs.rmSync(`${out}/picker-focus-scratch-1280-${theme}.png`);
  const style = await apple.evaluate((el) => {
    const s = getComputedStyle(el);
    return { background: s.backgroundColor, color: s.color, height: s.height, classes: el.className, text: el.innerText };
  });
  console.log(`apple button, ${theme}:`, JSON.stringify(style));
  const other = await visitor.locator('a[data-connector=github]').evaluate((el) => {
    const s = getComputedStyle(el);
    return { height: s.height, width: s.width };
  });
  const appleBox = await apple.evaluate((el) => { const s = getComputedStyle(el); return { height: s.height, width: s.width }; });
  console.log(`sizes, ${theme}: apple ${JSON.stringify(appleBox)} github ${JSON.stringify(other)}`);
}

console.log(metrics.filter((m) => !m.includes('scratch')).join('\n'));
await browser.close();
