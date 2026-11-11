// Capture walk for task b6d7 (spec 014), part B: the Apple button's logo.
// Args: base URL, setup token, output folder, path of a P-256 private key in PEM (a throwaway one).
//
// The logo pasted here is a STAND-IN, not Apple's artwork (which may not be bundled or
// committed): a made-up rounded shape laid out as Apple's files are, a small mark centred in
// a tall box with its own padding, so that the button's proportions can be judged.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, setupToken, out, keyPath] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const credential = 'a-very-good-credential-b6d7';
const pem = fs.readFileSync(keyPath, 'utf8');
const themes = ['dark', 'light'];
const widths = [1280, 360];
const metrics = [];

const standIn = `<?xml version="1.0" encoding="UTF-8"?>
<svg width="31px" height="44px" viewBox="0 0 31 44" version="1.1" xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink">
    <title>Stand-in logo</title>
    <g id="Black" stroke="none" stroke-width="1" fill="none" fill-rule="evenodd">
        <rect id="Rectangle" fill="#000000" x="0" y="0" width="31" height="44"></rect>
        <path d="M15.5 14 C19.5 14 22 17 22 21.5 C22 26 19.5 30 15.5 30 C11.5 30 9 26 9 21.5 C9 17 11.5 14 15.5 14 Z M16 13 C16 11 17.5 9.5 19.5 9.5 C19.5 11.5 18 13 16 13 Z" fill="#FFFFFF" fill-rule="nonzero"></path>
    </g>
</svg>`;

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
  const r = await page.request.post(base + path, { form: { __RequestVerificationToken: t, ...form }, headers: { Origin: base }, maxRedirects: 0 });
  return r.headers()['location'] ?? `status ${r.status()}`;
}
async function setTheme(theme) {
  await context.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
}
async function shot(name, width, theme) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  metrics.push(`${name}-${width}-${theme}: overflow=${overflow}`);
  await page.screenshot({ path: `${out}/${name}-${width}-${theme}.png`, fullPage: true });
}

const provider = (displayName, clientId) => ({ displayName, clientId, clientSecret: 'not-a-real-secret-0123456789', shared: 'true', allowedDomains: '' });
console.log('github:', await post('/admin/connectors/github', provider('GitHub', 'Iv1.0123456789abcdef')));
console.log('google:', await post('/admin/connectors/google', provider('Google', '123-abc.apps.googleusercontent.com')));
console.log('discord:', await post('/admin/connectors/discord', provider('Discord', '123456789012345678')));
console.log('passkey:', await post('/admin/connectors/passkey', { enrolment: 'open', shared: 'on' }));

await page.goto(base + '/admin/connectors/apple');
const good = { displayName: 'Apple', servicesId: 'com.example.signin', teamId: 'DEF123GHIJ', keyId: 'ABC123DEFG', privateKey: pem };
for (const [name, value] of Object.entries(good)) await page.fill(`[name=${name}]`, value);
await Promise.all([page.waitForResponse((r) => r.request().method() === 'POST'), page.locator('form[action="/admin/connectors/apple"] button[type=submit]').first().click()]);
await page.waitForLoadState('networkidle');

// Paste through the pane's own form, so a refusal is the 422 page the operator sees.
async function paste(text) {
  await page.goto(base + '/admin/connectors/apple');
  await page.fill('[name=appleLogo]', text);
  const save = page.locator('form[action="/admin/connectors/apple/logo"] button[type=submit]');
  const [response] = await Promise.all([page.waitForResponse((r) => r.request().method() === 'POST'), save.click()]);
  await page.waitForLoadState('networkidle');
  return response.status();
}
async function paneSection() {
  await page.locator('[data-apple-logo-section]').scrollIntoViewIfNeeded();
}

// The pane with no logo, and a refusal.
for (const theme of themes) {
  await setTheme(theme);
  for (const width of widths) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(base + '/admin/connectors/apple');
    await page.waitForLoadState('networkidle');
    await paneSection();
    await shot('apple-pane-no-logo', width, theme);
    console.log('refused ->', await paste('<svg viewBox="0 0 1 1"><script>MARKER-b6d7</script><path d="M0 0Z"/></svg>'));
    await paneSection();
    await shot('apple-logo-refused', width, theme);
    console.log('marker in the refused page:', (await page.content()).includes('MARKER-b6d7'));
  }
}

// Apple without a logo on the picker, before the logo is pasted.
async function createApp(name, redirectUri) {
  const loc = await post('/admin/apps', { name, redirectUri, postLogoutRedirectUri: '', appType: 'web', connectorChoice: 'all-shared' });
  return decodeURIComponent(loc.split('?')[0].split('/').pop());
}
const plain = await createApp('Wiki', 'https://wiki.example.com/oauth/callback');
const branded = await createApp('Acme Notes', 'https://notes.example.com/oauth/callback');
console.log('accent:', await post(`/admin/apps/${branded}/branding/accent`, { accent: '#b4532a' }));

// The branded app's logo: a PNG drawn on a canvas in the page.
const pngBase64 = await page.evaluate(() => {
  const c = document.createElement('canvas');
  c.width = 240; c.height = 80;
  const g = c.getContext('2d');
  g.fillStyle = '#b4532a'; g.beginPath(); g.arc(40, 40, 30, 0, Math.PI * 2); g.fill();
  g.fillStyle = '#3a2a20'; g.font = 'bold 34px sans-serif'; g.fillText('Acme', 84, 52);
  return c.toDataURL('image/png').split(',')[1];
});
{
  const t = await token();
  const r = await page.request.post(`${base}/admin/apps/${branded}/branding/logo`, {
    multipart: { __RequestVerificationToken: t, logo: { name: 'acme.png', mimeType: 'image/png', buffer: Buffer.from(pngBase64, 'base64') } },
    headers: { Origin: base }, maxRedirects: 0,
  });
  console.log('app logo:', r.headers()['location'] ?? r.status());
}

function picker(clientId, redirectUri) {
  const q = new URLSearchParams({ client_id: clientId, redirect_uri: redirectUri, response_type: 'code', scope: 'openid', state: 'capture', code_challenge: 'E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM', code_challenge_method: 'S256' });
  return `${base}/signin?returnUrl=${encodeURIComponent(`/connect/authorize?${q}`)}`;
}
const plainUrl = picker(plain, 'https://wiki.example.com/oauth/callback');
const brandedUrl = picker(branded, 'https://notes.example.com/oauth/callback');

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

for (const theme of themes) for (const width of widths) {
  await open(width, theme, plainUrl); await visitorShot('picker-apple-no-logo', width, theme);
}

// Paste the stand-in logo.
await setTheme('dark');
console.log('paste stand-in ->', await paste(standIn), page.url());
for (const theme of themes) {
  await setTheme(theme);
  for (const width of widths) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(base + '/admin/connectors/apple');
    await page.waitForLoadState('networkidle');
    await paneSection();
    await shot('apple-pane-logo', width, theme);
  }
}
const stored = await page.locator('[data-apple-logo-sample] svg').evaluate((el) => el.outerHTML);
console.log('preview svg:', stored);

for (const theme of themes) {
  for (const width of widths) {
    await open(width, theme, plainUrl); await visitorShot('picker', width, theme);
    await open(width, theme, brandedUrl); await visitorShot('picker-accent', width, theme);
    await open(width, theme, plainUrl, 'apple'); await visitorShot('picker-last-used', width, theme);
  }
  await open(1280, theme, plainUrl);
  const facts = await visitor.locator('a[data-connector]').evaluateAll((els) => els.map((el) => {
    const r = el.getBoundingClientRect();
    const svg = el.querySelector('svg');
    const sr = svg ? svg.getBoundingClientRect() : null;
    const label = [...el.childNodes].filter((n) => n.nodeType === 3).map((n) => n.textContent.trim()).join('');
    return { slug: el.dataset.connector, button: `${Math.round(r.width)}x${Math.round(r.height)}`, mark: sr ? `${Math.round(sr.width)}x${Math.round(sr.height)} at ${Math.round(sr.left - r.left)}` : '(none)', label };
  }));
  console.log(`${theme}:`, JSON.stringify(facts));
}
// Focus on the Apple button with its logo, 1280 dark.
await open(1280, 'dark', plainUrl);
const apple = visitor.locator('a[data-connector=apple]');
for (let i = 0; i < 30; i++) {
  await visitor.keyboard.press('Tab');
  if (await apple.evaluate((el) => el === document.activeElement)) break;
}
await visitor.screenshot({ path: `${out}/picker-focus-1280-dark.png`, fullPage: true });

console.log(metrics.join('\n'));
await browser.close();
