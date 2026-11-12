// Capture walk for the design review of spec 015 (the Bluesky connector).
// Usage: node capture.mjs <mode> <base> <setup-token> <out> [hostRules]
//   mode A: instance with the development exception (http://127.0.0.1:PORT): pane, picker, step.
//   mode B: instance whose origin cannot work (http://localhost:PORT): connectors-origin-blocked.
//   mode C: instance with an https origin, reached through a host-resolver rule: connectors-any-https.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [mode, base, setupToken, out, hostRules] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const credential = 'a-very-good-credential-b5ky';
const themes = ['dark', 'light'];
const widths = [1280, 360];
const metrics = [];

const launchArgs = hostRules ? [`--host-resolver-rules=${hostRules}`] : [];
const browser = await chromium.launch({ channel: 'chrome', args: launchArgs });
const newContext = () => browser.newContext({ viewport: { width: 1280, height: 900 }, ignoreHTTPSErrors: true });
const context = await newContext();
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
async function setTheme(ctx, theme) {
  await ctx.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
}
async function shot(pg, name, width, theme, { fullPage = true } = {}) {
  await pg.waitForLoadState('networkidle');
  const overflow = await pg.evaluate(() => ({ sw: document.documentElement.scrollWidth, iw: window.innerWidth }));
  metrics.push(`${name}-${width}-${theme}: overflow=${overflow.sw > overflow.iw} (scrollWidth ${overflow.sw}, innerWidth ${overflow.iw})`);
  await pg.screenshot({ path: `${out}/${name}-${width}-${theme}.png`, fullPage });
}
// Take one adminpane shot per width and theme.
async function paneShots(name, prepare) {
  for (const theme of themes) {
    await setTheme(context, theme);
    for (const width of widths) {
      await page.setViewportSize({ width, height: 900 });
      await prepare();
      await shot(page, name, width, theme);
    }
  }
}
const paneUrl = base + '/admin/connectors/bluesky';
const openPane = async () => { await page.goto(paneUrl); };
// Save the Bluesky form in the page, so a refusal is the page the operator sees.
async function saveBluesky(scope, servers, shared = true) {
  await page.goto(paneUrl);
  await page.check(`input[name=scope][value=${scope}]`);
  await page.fill('[name=servers]', servers);
  const box = page.locator('input[name=shared]');
  if ((await box.isChecked()) !== shared) await box.setChecked(shared);
  const save = page.locator('form[action="/admin/connectors/bluesky"] button[type=submit]').first();
  const [response] = await Promise.all([page.waitForResponse((r) => r.request().method() === 'POST'), save.click()]);
  await page.waitForLoadState('networkidle');
  return `${response.status()} -> ${page.url()}`;
}

if (mode === 'B' || mode === 'C') {
  const text = await (async () => { await page.goto(paneUrl); await page.waitForLoadState('networkidle'); return page.locator('[data-frame]').first().innerText(); })();
  console.log(`pane text on ${mode}:\n${text}`);
  if (mode === 'B') {
    await paneShots('connectors-origin-blocked', openPane);
    console.log('origin-blocked alert present:', await page.locator('[data-origin-blocked]').count());
    console.log('settings form present:', await page.locator('form[action="/admin/connectors/bluesky"]').count());
    console.log('attempted save on blocked origin ->', await post('/admin/connectors/bluesky', { scope: 'any', shared: 'true', servers: '' }));
    await page.goto(paneUrl);
    console.log('after the crafted post, state:', await page.locator('[data-connector-state]').count(), await page.locator('[data-frame]').first().innerText().then((s) => s.split('\n').slice(0, 4).join(' | ')));
  } else {
    await setTheme(context, 'dark');
    await page.setViewportSize({ width: 1280, height: 900 });
    console.log('save on https ->', await saveBluesky('any', ''));
    await page.goto(paneUrl);
    await shot(page, 'connectors-any-https', 1280, 'dark');
    console.log('metadata field:', await page.locator('[data-client-metadata-url]').first().inputValue().catch((e) => 'none: ' + e.message.split('\n')[0]));
  }
  console.log(metrics.join('\n'));
  await browser.close();
  process.exit(0);
}

// ---- Mode A ---------------------------------------------------------------

// 1. The pane, not set up.
await paneShots('connectors-off', openPane);
console.log('dev note present:', await page.locator('[data-dev-loopback-note]').count());
await page.goto(paneUrl);
console.log('pane (off) text:\n' + await page.locator('[data-frame]').first().innerText());

// 2. The list entry for Bluesky among the others, with another provider and Fediverse enabled
//    so the picker shows all three.
console.log('github connector:', await post('/admin/connectors/github', {
  displayName: 'GitHub', clientId: 'Iv1.0123456789abcdef', clientSecret: 'not-a-real-secret-0123456789', shared: 'true', allowedDomains: '',
}));
console.log('fediverse connector:', await post('/admin/connectors/fediverse', { scope: 'any', shared: 'true', servers: '' }));

// 3. Enabled, Any server.
console.log('save any ->', await saveBluesky('any', ''));
await paneShots('connectors-any', openPane);
console.log('pane (any) text:\n' + await page.locator('[data-frame]').first().innerText());

// 4. Enabled, Only these servers with two servers.
console.log('save list ->', await saveBluesky('listed', 'bsky.social, pds.example.org'));
await paneShots('connectors-list', openPane);

// 5. A refused entry, drawn under the field.
console.log('save refused ->', await saveBluesky('listed', '*.bsky.social'));
for (const theme of themes) {
  await setTheme(context, theme);
  for (const width of widths) {
    await page.setViewportSize({ width, height: 900 });
    // Redraw the refusal each time: the 422 page is the answer to a post, not a url.
    console.log(`refused ${width}-${theme}:`, await saveBluesky('listed', '*.bsky.social'));
    await shot(page, 'connectors-list-refused', width, theme);
  }
}
console.log('refusal text:', await page.locator('[aria-invalid=true]').first().evaluate((el) => el.closest('div')?.innerText).catch(() => 'n/a'));

// 6. Two apps: one plain, one with an accent colour.
async function createApp(name, redirectUri) {
  const loc = await post('/admin/apps', { name, redirectUri, postLogoutRedirectUri: '', appType: 'web', connectorChoice: 'all-shared' });
  console.log('create', name, loc);
  return decodeURIComponent(loc.split('?')[0].split('/').pop());
}
// Make the pane save good again before the apps: the list has bsky.social and pds.example.org.
console.log('save list again ->', await saveBluesky('listed', 'bsky.social, pds.example.org'));
const plain = await createApp('Wiki', 'https://wiki.example.com/oauth/callback');
const branded = await createApp('Acme Notes', 'https://notes.example.com/oauth/callback');
console.log('accent:', await post(`/admin/apps/${branded}/branding/accent`, { accent: '#b4532a' }));
// A logo for the branded app: drawn in the page, uploaded through the real form's endpoint.
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

function authorize(clientId, redirectUri) {
  const q = new URLSearchParams({
    client_id: clientId, redirect_uri: redirectUri, response_type: 'code', scope: 'openid',
    state: 'capture', code_challenge: 'E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM', code_challenge_method: 'S256',
  });
  return `${base}/connect/authorize?${q}`;
}
const plainAuth = authorize(plain, 'https://wiki.example.com/oauth/callback');
const brandedAuth = authorize(branded, 'https://notes.example.com/oauth/callback');
// The step and the picker take a returnUrl (the app's authorize request, relative).
const rel = (u) => u.slice(base.length);

// A signed-out visitor.
const visitorContext = await newContext();
const visitor = await visitorContext.newPage();
// /signin and /connect/* share a limit of 30 requests a minute, so each request waits.
const pace = () => new Promise((r) => setTimeout(r, 2200));
async function visit(url, theme, width) {
  await pace();
  await visitorContext.clearCookies();
  await setTheme(visitorContext, theme);
  await visitor.setViewportSize({ width, height: 900 });
  await visitor.goto(url);
  await visitor.waitForLoadState('networkidle');
}

// The picker (the app's own authorize request, the way an app sends people).
await visitor.goto(plainAuth);
console.log('authorize lands on:', visitor.url());
const pickerUrl = (auth) => `${base}/signin?returnUrl=${encodeURIComponent(rel(auth))}`;
await visitor.goto(pickerUrl(plainAuth));
console.log('picker buttons:', JSON.stringify(await visitor.locator('a[data-connector]').evaluateAll((els) => els.map((e) => ({ c: e.dataset.connector, t: e.innerText.trim() })))));

for (const theme of themes) {
  for (const width of widths) {
    await visit(pickerUrl(plainAuth), theme, width);
    await shot(visitor, 'picker', width, theme);
    if (width === 1280) { await visit(pickerUrl(brandedAuth), theme, width); await shot(visitor, 'picker-branded', width, theme); }
  }
}
const sizes = await visitor.locator('a[data-connector]').evaluateAll((els) => els.map((e) => { const r = e.getBoundingClientRect(); return `${e.dataset.connector}: ${Math.round(r.width)}x${Math.round(r.height)}`; }));
console.log('button sizes at last viewport:', sizes.join(', '));

// The step.
const stepUrl = (auth) => `${base}/connect/bluesky/start?returnUrl=${encodeURIComponent(rel(auth))}`;
async function typeAndSubmit(handle) {
  await pace();
  await visitor.fill('[name=handle]', handle);
  const [response] = await Promise.all([
    visitor.waitForResponse((r) => r.request().method() === 'POST' && r.url().includes('/connect/bluesky/start')),
    visitor.press('[name=handle]', 'Enter'),
  ]);
  await visitor.waitForLoadState('networkidle');
  return response.status();
}
const errorText = () => visitor.locator('[aria-invalid=true]').first().evaluate((el) => (el.closest('div')?.parentElement ?? el.parentElement).innerText).catch(() => 'none');
const stepText = {};

for (const theme of themes) {
  for (const width of widths) {
    await visit(stepUrl(plainAuth), theme, width);
    await shot(visitor, 'step', width, theme);
    if (width === 1280) { await visit(stepUrl(brandedAuth), theme, width); await shot(visitor, 'step-branded', width, theme); }
    // Not a handle
    await visit(stepUrl(plainAuth), theme, width);
    console.log(`not-a-handle ${width}-${theme}:`, await typeAndSubmit('not a handle'));
    await shot(visitor, 'step-not-a-handle', width, theme);
    stepText['not-a-handle'] = await errorText();
    // Not found
    await visit(stepUrl(plainAuth), theme, width);
    console.log(`not-found ${width}-${theme}:`, await typeAndSubmit('nobody-here.invalid-example-domain.com'));
    await shot(visitor, 'step-not-found', width, theme);
    stepText['not-found'] = await errorText();
  }
}
// Focus ring: keyboard focus on the field, 1280 dark.
await visit(stepUrl(plainAuth), 'dark', 1280);
console.log('field focused on load (autofocus):', await visitor.locator('[name=handle]').evaluate((el) => el === document.activeElement));
for (let i = 0; i < 5 && !(await visitor.locator('[name=handle]').evaluate((el) => el === document.activeElement)); i++) await visitor.keyboard.press('Tab');
console.log('field focused:', await visitor.locator('[name=handle]').evaluate((el) => el === document.activeElement));
await shot(visitor, 'step-focus', 1280, 'dark');
// Focus after a refusal (the spec says focus goes back to the field).
await visit(stepUrl(plainAuth), 'dark', 1280);
await typeAndSubmit('not a handle');
console.log('focus after refusal on field:', await visitor.locator('[name=handle]').evaluate((el) => el === document.activeElement));

// step-refused: Only these servers with pds.example.org, then a real public handle on bsky.social.
console.log('save list pds only ->', await saveBluesky('listed', 'pds.example.org'));
let refusedOk = true;
for (const theme of themes) {
  for (const width of widths) {
    await visit(stepUrl(plainAuth), theme, width);
    let status;
    try { status = await typeAndSubmit('bsky.app'); } catch (e) { status = 'error ' + e.message.split('\n')[0]; refusedOk = false; }
    console.log(`refused ${width}-${theme}:`, status);
    await shot(visitor, 'step-refused', width, theme);
    stepText['refused'] = await errorText();
  }
}
console.log('error texts:', JSON.stringify(stepText, null, 1));
console.log('refused ok:', refusedOk);
// Leave the connector as the list the review asked for.
await saveBluesky('listed', 'bsky.social, pds.example.org');

console.log('\nMETRICS\n' + metrics.join('\n'));
await browser.close();
