// Retake of the Microsoft connector states that changed after the design review (task m5ft).
// Usage: node retake.mjs <phase> <base> <setup-token|-> <out>
//   phase a: instance with network, fresh data directory (first-run setup): rule-line-saved,
//            rule-line-list, refused-tenant-unknown; ends with a saved "one organization" connector.
//   phase b: the same data directory, instance restarted with HTTPS_PROXY=http://127.0.0.1:9 so
//            Umbral cannot reach login.microsoftonline.com: unchanged-unchecked, refused-unanswered.
// Helpers are those of capture.mjs.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';
import crypto from 'node:crypto';

const [phase, base, setupToken, out] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const credential = 'a-very-good-credential-m5ft';
const themes = ['dark', 'light'];
const widths = [1280, 360];
const metrics = [];
const CLIENT_ID = '3f1d7a52-9c4e-4b1a-8d2e-6a7b5c4d3e21';
const SECRET = 'not-a-real-secret~0123456789';
const TENANT = '72f988bf-86f1-41af-91ab-2d7cd011db47'; // a real tenant GUID (Microsoft's own)

const browser = await chromium.launch({ channel: 'chrome' });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await context.newPage();

// First-run setup (phase a) or sign-in (phase b) through the real forms.
if (phase === 'a') {
  await page.goto(base + '/admin/setup');
  await page.fill('[name=token]', setupToken);
  await page.fill('[name=credential]', credential);
  await page.fill('[name=confirm]', credential);
  await page.press('[name=confirm]', 'Enter');
  await page.waitForLoadState('networkidle');
}
if (page.url().includes('/admin/signin') || phase === 'b') {
  if (!page.url().includes('/admin/signin')) await page.goto(base + '/admin/signin');
  await page.fill('[name=credential]', credential);
  await page.press('[name=credential]', 'Enter');
  await page.waitForLoadState('networkidle');
}
console.log('after login:', page.url());

async function setTheme(ctx, theme) {
  await ctx.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
}
async function shot(pg, name, width, theme, { fullPage = true } = {}) {
  await pg.waitForLoadState('networkidle');
  const overflow = await pg.evaluate(() => ({ sw: document.documentElement.scrollWidth, iw: window.innerWidth }));
  metrics.push(`${name}-${width}-${theme}: overflow=${overflow.sw > overflow.iw} (scrollWidth ${overflow.sw}, innerWidth ${overflow.iw})`);
  await pg.screenshot({ path: `${out}/${name}-${width}-${theme}.png`, fullPage });
}
const paneUrl = base + '/admin/connectors/microsoft';
const frameText = () => page.locator('[data-frame]').first().innerText();

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
// One shot per width and theme, each its own save (a notice is a one-time banner; a 422 is a post).
async function eachSave(name, combos, fields, expectStatus) {
  for (const [width, theme] of combos) {
    await setTheme(context, theme);
    await page.setViewportSize({ width, height: 900 });
    const r = await saveMicrosoft(fields());
    if (!r.startsWith(expectStatus)) console.log(`!! ${name} ${width}-${theme} unexpected:`, r);
    else console.log(`${name} ${width}-${theme}: ${r}`);
    await shot(page, name, width, theme);
  }
}
const all = themes.flatMap((theme) => widths.map((width) => [width, theme]));
const ruleLine = () => page.locator('[data-rule], [data-rule-line], main, body').first().innerText().then((s) => s.split('\n').filter((l) => l.startsWith('Who can sign in')));

if (phase === 'a') {
  // 1. A saved personal-accounts connector; the pane is a fresh load.
  console.log('save personal ->', await saveMicrosoft({ displayName: 'Microsoft', clientId: CLIENT_ID, secret: SECRET, audience: 'consumers', shared: true, domains: '' }));
  for (const [width, theme] of all) {
    await setTheme(context, theme);
    await page.setViewportSize({ width, height: 900 });
    await page.goto(paneUrl);
    await shot(page, 'rule-line-saved', width, theme);
  }
  console.log('rule line (saved):', JSON.stringify(await ruleLine()));

  // 2. The same connector with its own allowed domains.
  console.log('save domains ->', await saveMicrosoft({ domains: 'example.org' }));
  await setTheme(context, 'dark');
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto(paneUrl);
  console.log('rule line (list):', JSON.stringify(await ruleLine()));
  await shot(page, 'rule-line-list', 1280, 'dark');
  console.log('clear domains ->', await saveMicrosoft({ domains: '' }));

  // 3. Changed to one organization with a GUID that names no tenant: refused, nothing saved.
  const unknown = crypto.randomUUID();
  console.log('random tenant GUID:', unknown);
  await eachSave('refused-tenant-unknown', all, () => ({ audience: 'tenant', tenantId: unknown }), '422');
  console.log('tenant error messages:', JSON.stringify(await page.locator('.um-field-error').allInnerTexts()));
  console.log('invalid fields:', await page.locator('[aria-invalid=true]').evaluateAll((els) => els.map((e) => `${e.tagName}[name=${e.getAttribute('name')}] describedby=${e.getAttribute('aria-describedby')}`)));
  console.log('checked card on the 422:', await page.locator('input[name=audience]:checked').getAttribute('value'), 'tenant field:', await page.locator('[name=tenantId]').inputValue());
  await page.goto(paneUrl);
  console.log('after the refusal the pane holds:', await page.locator('input[name=audience]:checked').getAttribute('value'), '/ state', await page.locator('[data-connector-state]').first().innerText().catch(() => 'n/a'));

  // 4. Leave a saved "one organization" connector for phase b.
  console.log('save one org ->', await saveMicrosoft({ audience: 'tenant', tenantId: TENANT }));
  console.log('state:', await page.locator('[data-connector-state]').first().innerText().catch(() => 'n/a'));
} else {
  await page.goto(paneUrl);
  console.log('pane at start of phase b:', (await frameText()).split('\n').slice(0, 12).join(' | '));
  console.log('stored card:', await page.locator('input[name=audience]:checked').getAttribute('value'), 'tenant:', await page.locator('[name=tenantId]').inputValue());

  // 5. Saved again, unchanged, while Microsoft cannot be reached.
  await eachSave('unchanged-unchecked', all, () => ({}), '302');
  console.log('notice (unchecked):', await page.locator('[role=alert], [data-notice]').first().innerText().catch(() => 'n/a'));
  console.log('state pill after:', await page.locator('[data-connector-state]').first().innerText().catch(() => 'n/a'));
  console.log('buttons after:', JSON.stringify(await page.locator('main button, form button').allInnerTexts()));

  // 6. The choice changed (to personal) while Microsoft cannot be reached: refused.
  await eachSave('refused-unanswered', [[1280, 'dark'], [360, 'light']], () => ({ audience: 'consumers' }), '422');
  console.log('unanswered messages:', JSON.stringify(await page.locator('.um-field-error').allInnerTexts()));
  console.log('invalid fields:', await page.locator('[aria-invalid=true]').evaluateAll((els) => els.map((e) => `${e.tagName}[name=${e.getAttribute('name')}] describedby=${e.getAttribute('aria-describedby')}`)));
  await page.goto(paneUrl);
  console.log('after the refusal the pane holds:', await page.locator('input[name=audience]:checked').getAttribute('value'), 'tenant', await page.locator('[name=tenantId]').inputValue());
}

console.log('\nMETRICS\n' + metrics.join('\n'));
await browser.close();
