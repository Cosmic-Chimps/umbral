// Capture walk for the design review of spec 016 (the Microsoft connector).
// Usage: node capture.mjs <base> <setup-token> <out>
//   One instance with the development exception (http://127.0.0.1:PORT).
// Saving the Microsoft pane makes Umbral fetch Microsoft's real discovery document,
// so the machine needs network.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, setupToken, out] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const credential = 'a-very-good-credential-m5ft';
const themes = ['dark', 'light'];
const widths = [1280, 360];
const metrics = [];
const CLIENT_ID = '3f1d7a52-9c4e-4b1a-8d2e-6a7b5c4d3e21';
const SECRET = 'not-a-real-secret~0123456789';
const TENANT = '72f988bf-86f1-41af-91ab-2d7cd011db47'; // a real tenant GUID (Microsoft's own)
const PERSONAL_TENANT = '9188040d-6c67-4c5b-b112-36a304b66dad';

const browser = await chromium.launch({ channel: 'chrome' });
const newContext = () => browser.newContext({ viewport: { width: 1280, height: 900 } });
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
const paneUrl = base + '/admin/connectors/microsoft';
const openPane = async () => { await page.goto(paneUrl); };
const openList = async () => { await page.goto(base + '/admin/connectors'); };

// Fill and save the Microsoft form in the page, so a refusal is the page the operator sees.
// Only the keys given are touched; the rest keep the pane's own values.
async function saveMicrosoft({ displayName, clientId, secret, audience, tenantId, shared, domains } = {}) {
  await page.goto(paneUrl);
  const form = page.locator('form[action="/admin/connectors/microsoft"]').first();
  if (displayName !== undefined) await form.locator('[name=displayName]').fill(displayName);
  if (clientId !== undefined) await form.locator('[name=clientId]').fill(clientId);
  if (secret !== undefined) await form.locator('[name=clientSecret]').fill(secret);
  if (audience) await form.locator(`input[name=audience][value=${audience}]`).check();
  if (tenantId !== undefined) await form.locator('[name=tenantId]').fill(tenantId);
  if (shared !== undefined) {
    const box = form.locator('input[name=shared]');
    if ((await box.isChecked()) !== shared) await box.setChecked(shared);
  }
  if (domains !== undefined) await form.locator('[name=allowedDomains]').fill(domains);
  const save = form.locator('button[type=submit]').first();
  const [response] = await Promise.all([page.waitForResponse((r) => r.request().method() === 'POST'), save.click()]);
  await page.waitForLoadState('networkidle');
  return `${response.status()} -> ${page.url()}`;
}
const frameText = () => page.locator('[data-frame]').first().innerText();

// 1. The list, Microsoft not set up.
await paneShots('connectors-list', openList);
await openList();
console.log('list text:\n' + await page.locator('main').first().innerText().catch(() => frameText()));

// 2. The pane, not set up.
await paneShots('pane-not-set-up', openPane);
await page.goto(paneUrl);
console.log('pane (not set up) text:\n' + await frameText());
console.log('radios checked on a new connector:', await page.locator('input[name=audience]:checked').count(), 'of', await page.locator('input[name=audience]').count());

// Keyboard focus on a radio card: Tab from the secret field lands on the first card.
for (const theme of ['dark', 'light']) {
  await setTheme(context, theme);
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto(paneUrl);
  await page.locator('form[action="/admin/connectors/microsoft"] [name=clientSecret]').focus();
  await page.keyboard.press('Tab');
  const focused = await page.evaluate(() => { const a = document.activeElement; return `${a.tagName} ${a.name}=${a.value}`; });
  console.log(`focus after Tab from the secret (${theme}):`, focused);
  await page.locator('input[name=audience]:focus').scrollIntoViewIfNeeded().catch(() => {});
  await shot(page, 'card-focus', 1280, theme, { fullPage: false });
}

// 3. The refusals (a new connector, so the 422 is the page of a refused first save).
async function refused(name, fields, describe) {
  for (const theme of themes) {
    await setTheme(context, theme);
    for (const width of widths) {
      await page.setViewportSize({ width, height: 900 });
      const r = await saveMicrosoft(fields);
      if (!r.startsWith('422')) console.log(`!! ${name} ${width}-${theme} did not come back 422:`, r);
      await shot(page, name, width, theme);
    }
  }
  console.log(`${name}:`, await page.locator('[aria-invalid=true]').evaluateAll((els) => els.map((e) => `${e.tagName}[name=${e.getAttribute('name')}] describedby=${e.getAttribute('aria-describedby')}`)));
  console.log(`${name} messages:`, JSON.stringify(await page.locator('.um-field-error').allInnerTexts()));
  if (describe) await describe();
}
await refused('refused-no-choice', { displayName: 'Microsoft', clientId: CLIENT_ID, secret: SECRET, shared: true }, async () => {
  // The refusal under the group is announced: every card points at it.
  console.log('announced:', await page.evaluate(() => {
    const radios = [...document.querySelectorAll('input[name=audience]')];
    const target = document.getElementById('audience-error');
    return { radios: radios.length, invalid: radios.filter((r) => r.getAttribute('aria-invalid') === 'true').length,
      describedby: [...new Set(radios.map((r) => r.getAttribute('aria-describedby')))], errorText: target?.innerText?.trim() };
  }));
});
await refused('refused-tenant-missing', { clientId: CLIENT_ID, secret: SECRET, audience: 'tenant', tenantId: '' });
await refused('refused-tenant-not-guid', { clientId: CLIENT_ID, secret: SECRET, audience: 'tenant', tenantId: 'contoso.onmicrosoft.com' });
await refused('refused-tenant-personal', { clientId: CLIENT_ID, secret: SECRET, audience: 'tenant', tenantId: PERSONAL_TENANT });
await refused('refused-several', { clientId: 'not-a-guid', secret: SECRET, audience: undefined, tenantId: '' });

// 4. Saved and enabled, one audience at a time.
console.log('save personal ->', await saveMicrosoft({ displayName: 'Microsoft', clientId: CLIENT_ID, secret: SECRET, audience: 'consumers', shared: true }));
console.log('pane (personal) after save:\n' + await frameText());
console.log('state pill:', await page.locator('[data-connector-state]').first().innerText().catch(() => 'n/a'));
await paneShots('pane-set-up', openPane);
await paneShots('card-personal', openPane);
console.log('checked:', await page.locator('input[name=audience]:checked').getAttribute('value'));
console.log('secret box value:', JSON.stringify(await page.locator('form[action="/admin/connectors/microsoft"] [name=clientSecret]').inputValue()));

console.log('save one org ->', await saveMicrosoft({ audience: 'tenant', tenantId: TENANT }));
console.log('pane (one org) after save, notice:', (await frameText()).split('\n').slice(0, 6).join(' | '));
await paneShots('card-one', openPane);
console.log('checked:', await page.locator('input[name=audience]:checked').getAttribute('value'), 'tenant field:', await page.locator('[name=tenantId]').inputValue());

console.log('save any org ->', await saveMicrosoft({ audience: 'organizations' }));
await paneShots('card-any', openPane);
console.log('tenant field kept:', await page.locator('[name=tenantId]').inputValue());

console.log('save both ->', await saveMicrosoft({ audience: 'common' }));
await paneShots('card-both', openPane);

// A tenant GUID that names no tenant: Microsoft's discovery answers with an error.
// The notice is a redirect banner, so each shot is its own save, with the theme set first.
for (const theme of themes) {
  await setTheme(context, theme);
  for (const width of widths) {
    await page.setViewportSize({ width, height: 900 });
    console.log(`save unknown tenant ${width}-${theme} ->`, await saveMicrosoft({ audience: 'tenant', tenantId: '11111111-2222-4333-8444-555555555555' }));
    await shot(page, 'notice-tenant-unknown', width, theme);
  }
}
console.log('notice (unknown tenant):', await page.locator('[role=alert], [data-notice]').first().innerText().catch(() => 'n/a'));
console.log('buttons in that state:', JSON.stringify(await page.locator('main button, form button').allInnerTexts()));
console.log('pill in that state:', await page.locator('text=Enabled').count(), 'Enabled labels;', await page.locator('text=Disabled').count(), 'Disabled labels');

// Back to Personal accounts, enabled, for the list and the picker.
console.log('save personal again ->', await saveMicrosoft({ audience: 'consumers' }));
console.log('state after:', await page.locator('[data-connector-state]').first().innerText().catch(() => 'n/a'));
await paneShots('connectors-list-set-up', openList);

// 5. Two other connectors so the picker shows several, and two apps.
console.log('github connector:', await post('/admin/connectors/github', {
  displayName: 'GitHub', clientId: 'Iv1.0123456789abcdef', clientSecret: 'not-a-real-secret-0123456789', shared: 'true', allowedDomains: '',
}));
console.log('fediverse connector:', await post('/admin/connectors/fediverse', { scope: 'any', shared: 'true', servers: '' }));
async function createApp(name, redirectUri) {
  const loc = await post('/admin/apps', { name, redirectUri, postLogoutRedirectUri: '', appType: 'web', connectorChoice: 'all-shared' });
  console.log('create', name, loc);
  return decodeURIComponent(loc.split('?')[0].split('/').pop());
}
const plain = await createApp('Wiki', 'https://wiki.example.com/oauth/callback');
const branded = await createApp('Acme Notes', 'https://notes.example.com/oauth/callback');
console.log('accent:', await post(`/admin/apps/${branded}/branding/accent`, { accent: '#b4532a' }));
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
const rel = (u) => u.slice(base.length);
const pickerUrl = (auth) => `${base}/signin?returnUrl=${encodeURIComponent(rel(auth))}`;

const visitorContext = await newContext();
const visitor = await visitorContext.newPage();
const pace = () => new Promise((r) => setTimeout(r, 2200)); // /signin and /connect/* share 30 requests a minute
async function visit(url, theme, width) {
  await pace();
  await visitorContext.clearCookies();
  await setTheme(visitorContext, theme);
  await visitor.setViewportSize({ width, height: 900 });
  await visitor.goto(url);
  await visitor.waitForLoadState('networkidle');
}
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

console.log('\nMETRICS\n' + metrics.join('\n'));
await browser.close();
