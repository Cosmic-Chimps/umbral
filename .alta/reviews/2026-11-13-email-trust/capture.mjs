// Capture walk for the design review of spec 017 (the trust setting on the OpenID Connect screens).
// Usage: node capture.mjs <base> <admin-credential> <out> <setup-token>
//   One fresh Release instance with the development exception (http://127.0.0.1:PORT), started with
//   HTTPS_PROXY=http://127.0.0.1:9. First-run setup is done through the real form (the
//   setup token from its log, passed as the fourth argument: the script claims it with the
//   credential given).
//   OpenID Connect providers are added at http://127.0.0.1:9/realmN, refused: saved disabled.
//   Everything goes through the browser forms (fill, click): the 422 pages are the response.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, credential, out, setupToken] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
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

async function setTheme(theme) {
  await context.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
}
async function shot(name, width, theme) {
  await page.waitForLoadState('networkidle');
  const o = await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, iw: window.innerWidth }));
  metrics.push(`${name}-${width}-${theme}: overflow=${o.sw > o.iw} (scrollWidth ${o.sw}, innerWidth ${o.iw})`);
  await page.screenshot({ path: `${out}/${name}-${width}-${theme}.png`, fullPage: true });
}
const text = (sel) => page.locator(sel).first().innerText().catch(() => '(none)');
async function each(name, prepare, report) {
  for (const theme of themes) {
    await setTheme(theme);
    for (const width of widths) {
      await page.setViewportSize({ width, height: 900 });
      const status = await prepare();
      if (report) console.log(`${name} ${width} ${theme}${status ? ' [' + status + ']' : ''}: ${await report()}`);
      await shot(name, width, theme);
    }
  }
}
const addForm = 'form[data-add-connector]';
async function fillAdd({ slug, name, issuer, trust }) {
  await page.goto(base + '/admin/connectors/new');
  await page.selectOption('#add-kind', 'oidc');
  await page.fill('#slug', slug);
  await page.fill('#displayName', name);
  await page.fill('#issuer', issuer);
  await page.fill('#clientId', 'umbral-client');
  await page.fill('#clientSecret', 'not-a-real-secret-017');
  if (trust) await page.locator(`${addForm} input[name=emailTrust][value=${trust}]`).check();
}
const submitAdd = async () => {
  const [r] = await Promise.all([
    page.waitForResponse((x) => x.request().method() === 'POST' && x.url().endsWith('/admin/connectors')),
    page.locator(`${addForm} button[type=submit]`).click(),
  ]);
  await page.waitForLoadState('networkidle');
  return `POST ${r.status()}`;
};
const group = () => page.locator('fieldset[data-email-trust]').first().innerText().then((t) => t.replace(/\s+/g, ' ').slice(0, 400));
const paneForm = (slug) => `form[action="/admin/connectors/${slug}"]`;
async function submitPane(slug) {
  const [r] = await Promise.all([
    page.waitForResponse((x) => x.request().method() === 'POST' && x.url().includes(`/admin/connectors/${slug}`)),
    page.locator(`${paneForm(slug)} button[type=submit]`).first().click(),
  ]);
  await page.waitForLoadState('networkidle');
  return `POST ${r.status()}`;
}
async function setInstanceList(value) {
  await page.goto(base + '/admin/connectors/domains');
  await page.fill('#allowedDomains', value);
  await page.locator('form[action="/admin/allowed-domains"] button[type=submit]').click();
  await page.waitForLoadState('networkidle');
  // Removing a list asks first: "Remove the list anyway".
  const anyway = page.getByRole('button', { name: 'Remove the list anyway' });
  if (await anyway.count()) {
    await anyway.click();
    await page.waitForLoadState('networkidle');
  }
  await page.goto(base + '/admin/connectors/domains');
  console.log(`instance list now: ${JSON.stringify(await page.inputValue('#allowedDomains'))}`);
}

// 1. The add form, OpenID Connect selected, neither card checked.
await each('add-nochoice', async () => { await fillAdd({ slug: 'newidp', name: 'New IdP', issuer: 'http://127.0.0.1:9/realm9' }); }, group);
// 1b. The same, with everything filled, submitted without a choice: 422.
await each('add-refused', async () => {
  await fillAdd({ slug: 'newidp', name: 'New IdP', issuer: 'http://127.0.0.1:9/realm9' });
  return await submitAdd();
}, async () => `${page.url()} :: ${(await page.locator('main').innerText()).match(/Choose whether[^\n]*/)?.[0] ?? '(refusal text not found)'}`);
// 2. Each card chosen, before submitting.
await each('add-on', async () => { await fillAdd({ slug: 'newidp', name: 'New IdP', issuer: 'http://127.0.0.1:9/realm9', trust: 'on' }); }, group);
await each('add-off', async () => { await fillAdd({ slug: 'newidp', name: 'New IdP', issuer: 'http://127.0.0.1:9/realm9', trust: 'off' }); }, group);
// 2b. Discord is the selected kind: does the group show?
await setTheme('light');
await page.setViewportSize({ width: 1280, height: 900 });
await page.goto(base + '/admin/connectors/new');
await page.selectOption('#add-kind', 'discord');
console.log(`add-discord 1280 light: group visible = ${await page.locator('fieldset[data-email-trust]').first().isVisible()}`);
await shot('add-discord', 1280, 'light');

// The two connectors: one trusted, one not trusted (saved disabled: the issuers are refused).
await fillAdd({ slug: 'trusted', name: 'Trusted IdP', issuer: 'http://127.0.0.1:9/realm1', trust: 'on' });
console.log('add trusted: ' + (await submitAdd()) + ' ' + page.url());
await fillAdd({ slug: 'untrusted', name: 'Untrusted forge', issuer: 'http://127.0.0.1:9/realm2', trust: 'off' });
console.log('add untrusted: ' + (await submitAdd()) + ' ' + page.url());

// 3. The pane of the trusted connector.
await each('pane', async () => { await page.goto(base + '/admin/connectors/trusted'); }, group);
// 4. An allowed domain typed, Not trusted chosen, saved: 422 under the domains field.
await each('pane-ownlist-refused', async () => {
  await page.goto(base + '/admin/connectors/trusted');
  await page.fill(`${paneForm('trusted')} #allowedDomains, ${paneForm('trusted')} [name=allowedDomains]`, 'example.org');
  await page.locator(`${paneForm('trusted')} input[name=emailTrust][value=off]`).check();
  return await submitPane('trusted');
}, async () => `${page.url()} :: ${(await page.locator('main').innerText()).match(/Allowed email domains need verified[^\n]*/)?.[0] ?? '(refusal text not found)'}`);
// 5. The instance list set; the connector saved Not trusted.
await setInstanceList('example.com');
await each('list-instance-off', async () => { await page.goto(base + '/admin/connectors'); }, async () => (await page.locator('main').innerText()).replace(/\s+/g, ' ').slice(0, 300));
await each('pane-instance-off', async () => { await page.goto(base + '/admin/connectors/untrusted'); },
  async () => (await page.locator('main').innerText()).match(/Nobody can sign in[^\n]*/g)?.join(' || ') ?? '(not found)');
// 6. The instance list cleared.
await setInstanceList('');
await each('pane-off-nolist', async () => { await page.goto(base + '/admin/connectors/untrusted'); },
  async () => (await page.locator('main').innerText()).match(/Who can sign in[^\n]*/g)?.join(' || ') ?? '(not found)');
// 7. Turning trust off on the trusted connector: the confirm block.
await each('pane-confirm-off', async () => {
  await page.goto(base + '/admin/connectors/trusted');
  await page.locator(`${paneForm('trusted')} input[name=emailTrust][value=off]`).check();
  return await submitPane('trusted');
}, async () => `${page.url()} :: ${(await page.locator('main').innerText()).match(/Turning trust off[^\n]*/g)?.join(' || ') ?? '(not found)'}`);

console.log(metrics.join('\n'));
await browser.close();
