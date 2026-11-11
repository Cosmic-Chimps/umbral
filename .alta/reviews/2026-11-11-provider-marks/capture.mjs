// Capture walk for task b6d7 (spec 014, provider marks), part A.
// Args: base URL, setup token, output folder, path of a P-256 private key in PEM (a throwaway one).
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, setupToken, out, keyPath] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const credential = 'a-very-good-credential-b6d7';
const pem = fs.readFileSync(keyPath, 'utf8');
const themes = ['dark', 'light'];
const widths = [1280, 360];
const metrics = [];

const browser = await chromium.launch({ channel: 'chrome' });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await context.newPage();

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

// Providers with made-up credentials, in the order an operator might add them.
const provider = (displayName, clientId) => ({ displayName, clientId, clientSecret: 'not-a-real-secret-0123456789', shared: 'true', allowedDomains: '' });
console.log('github:', await post('/admin/connectors/github', provider('GitHub', 'Iv1.0123456789abcdef')));
console.log('google:', await post('/admin/connectors/google', provider('Google', '123-abc.apps.googleusercontent.com')));
console.log('discord:', await post('/admin/connectors/discord', provider('Discord', '123456789012345678')));
console.log('passkey:', await post('/admin/connectors/passkey', { enrolment: 'open', shared: 'on' }));

// Apple, through its pane (saved disabled if Apple is unreachable; enabled otherwise).
await page.goto(base + '/admin/connectors/apple');
const good = { displayName: 'Apple', servicesId: 'com.example.signin', teamId: 'DEF123GHIJ', keyId: 'ABC123DEFG', privateKey: pem };
for (const [name, value] of Object.entries(good)) await page.fill(`[name=${name}]`, value);
await Promise.all([page.waitForResponse((r) => r.request().method() === 'POST'), page.locator('form[action="/admin/connectors/apple"] button[type=submit]').first().click()]);
await page.waitForLoadState('networkidle');
console.log('apple notice:', JSON.stringify(await page.locator('[role=status], .um-alert').allInnerTexts()));

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
  // Straight to the picker with the authorize request as its returnUrl (as the tests do):
  // over plain http OpenIddict refuses /connect/authorize itself.
  return `${base}/signin?returnUrl=${encodeURIComponent(`/connect/authorize?${q}`)}`;
}
const plainUrl = authorize(plain, 'https://wiki.example.com/oauth/callback');
const brandedUrl = authorize(branded, 'https://notes.example.com/oauth/callback');

const signedOut = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const visitor = await signedOut.newPage();
async function open(width, theme, url, lastUsed) {
  const cookies = [{ name: 'umbral_theme', value: theme, url: base }];
  await signedOut.clearCookies();
  if (lastUsed) cookies.push({ name: 'umbral_last_connector', value: lastUsed, url: base });
  await signedOut.addCookies(cookies);
  await visitor.setViewportSize({ width, height: 900 });
  await visitor.goto(url);
  await visitor.waitForLoadState('networkidle');
}
async function visitorShot(name, width, theme) {
  const overflow = await visitor.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  metrics.push(`${name}-${width}-${theme}: overflow=${overflow}`);
  await visitor.screenshot({ path: `${out}/${name}-${width}-${theme}.png`, fullPage: true });
}

for (const theme of themes) {
  for (const width of widths) {
    await open(width, theme, plainUrl); await visitorShot('picker', width, theme);
    await open(width, theme, brandedUrl); await visitorShot('picker-accent', width, theme);
    await open(width, theme, plainUrl, 'github'); await visitorShot('picker-last-used', width, theme);
  }
  // The colours the marks are drawn in, and every button's size.
  for (const [label, url] of [['plain', plainUrl], ['accent', brandedUrl]]) {
    await open(1280, theme, url);
    const facts = await visitor.locator('a[data-connector]').evaluateAll((els) => els.map((el) => {
      const svg = el.querySelector('svg');
      const s = getComputedStyle(el);
      return { slug: el.dataset.connector, button: `${Math.round(el.getBoundingClientRect().width)}x${Math.round(el.getBoundingClientRect().height)}`, label: s.color, mark: svg ? getComputedStyle(svg).color : '(none)' };
    }));
    console.log(`${theme} ${label}:`, JSON.stringify(facts));
  }
}
// Keyboard focus on the GitHub button, 1280 dark.
await open(1280, 'dark', plainUrl);
const github = visitor.locator('a[data-connector=github]');
for (let i = 0; i < 30; i++) {
  await visitor.keyboard.press('Tab');
  if (await github.evaluate((el) => el === document.activeElement)) break;
}
await visitor.screenshot({ path: `${out}/picker-focus-github-1280-dark.png`, fullPage: true });

console.log(metrics.join('\n'));
await browser.close();
