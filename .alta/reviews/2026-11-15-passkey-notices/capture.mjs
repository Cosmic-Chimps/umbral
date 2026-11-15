// Capture walk for the design review of task njtn (Your passkeys: success notices drawn green).
// Usage: node capture.mjs <base> <setup-token> <out>
//   One fresh Release instance in Development (http://localhost:PORT, so WebAuthn accepts the
//   origin), started with HTTPS_PROXY=http://127.0.0.1:9. Setup goes through the real form, the
//   Passkey connector is enabled with open sign-up through its pane, and the person signs up with
//   Chrome's virtual authenticator. passkey-added and passkey-removed come from the real Add and
//   Remove; the two refusals are the same page with their ?msg= code.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, setupToken, out] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const credential = 'njtn-capture-operator-credential';
const metrics = [];

const browser = await chromium.launch({ channel: 'chrome' });

// The operator: setup, then the Passkey connector with open sign-up.
const opContext = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const op = await opContext.newPage();
await op.goto(base + '/admin/setup');
await op.fill('[name=token]', setupToken);
await op.fill('[name=credential]', credential);
await op.fill('[name=confirm]', credential);
await op.press('[name=confirm]', 'Enter');
await op.waitForLoadState('networkidle');
if (op.url().includes('/admin/signin')) {
  await op.fill('[name=credential]', credential);
  await op.press('[name=credential]', 'Enter');
  await op.waitForLoadState('networkidle');
}
await op.goto(base + '/admin/connectors/passkey');
const pane = op.locator('form:has(input[name=enrolment])').first();
await pane.locator('input[name=enrolment][value=open]').check();
const shared = pane.locator('input[name=shared][type=checkbox]');
if (await shared.count()) await shared.check();
await pane.locator('button[type=submit]').last().click();
await op.waitForLoadState('networkidle');
console.log('passkey connector saved: ' + op.url());

// The person, with a virtual authenticator.
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await context.newPage();
const cdp = await context.newCDPSession(page);
await cdp.send('WebAuthn.enable');
// One authenticator per key: an authenticator that already holds a key for the account refuses
// the next registration (excludeCredentials), as a real device would.
let authenticatorId = null;
async function freshAuthenticator() {
  if (authenticatorId) await cdp.send('WebAuthn.removeVirtualAuthenticator', { authenticatorId });
  ({ authenticatorId } = await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true,
               isUserVerified: true, automaticPresenceSimulation: true },
  }));
}
await freshAuthenticator();

await page.goto(base + '/connect/passkey/start?returnUrl=%2Faccount%2Fpasskeys');
await page.fill('form[data-passkey-signup] [name=displayName]', 'Sam Okoro');
await page.fill('form[data-passkey-signup] [name=nickname]', 'MacBook');
await Promise.all([page.waitForURL((u) => !u.pathname.startsWith('/connect/passkey')),
                   page.click('form[data-passkey-signup] button[type=submit]')]);
await page.waitForLoadState('networkidle');
console.log('signed up, now at ' + page.url());

async function addPasskey(nickname) {
  await freshAuthenticator();
  await page.goto(base + '/account/passkeys');
  await page.fill('form[data-passkey-add] [name=nickname]', nickname);
  await Promise.all([page.waitForURL(/msg=passkey-added/), page.click('form[data-passkey-add] button[type=submit]')]);
  await page.waitForLoadState('networkidle');
  return page.url();
}

async function removePasskey(nickname) {
  await page.goto(base + '/account/passkeys');
  const card = page.locator('[data-passkey]', { hasText: nickname });
  page.once('dialog', (d) => d.accept());
  await Promise.all([page.waitForURL(/msg=passkey-removed/), card.locator('button[type=submit]').click()]);
  await page.waitForLoadState('networkidle');
  return page.url();
}

async function shot(name, width, theme) {
  await context.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
  await page.setViewportSize({ width, height: 900 });
  await page.reload();
  await page.waitForLoadState('networkidle');
  const o = await page.evaluate(() => {
    const n = document.querySelector('[data-notice]');
    const a = n?.querySelector('.um-alert');
    return { sw: document.documentElement.scrollWidth, iw: window.innerWidth,
             notice: n?.getAttribute('data-notice') ?? null, cls: a?.className ?? null,
             role: a?.getAttribute('role') ?? null, text: a?.innerText.replace(/\s+/g, ' ') ?? null };
  });
  metrics.push(`${name}-${width}-${theme}: overflow=${o.sw > o.iw} (scrollWidth ${o.sw}, innerWidth ${o.iw}); ` +
               `notice=${o.notice} class="${o.cls}" role=${o.role} text="${o.text}"`);
  await page.screenshot({ path: `${out}/${name}-${width}-${theme}.png`, fullPage: true });
}
async function each(name) {
  for (const theme of ['dark', 'light']) for (const width of [1280, 360]) await shot(name, width, theme);
}

// The real Add: the ceremony sends the page back with ?msg=passkey-added.
console.log('added: ' + await addPasskey('Pixel 9'));
await each('notice-added');

// A spare to remove, so the page keeps two keys; the real Remove sends ?msg=passkey-removed.
console.log('added: ' + await addPasskey('Spare key'));
console.log('removed: ' + await removePasskey('Spare key'));
await each('notice-removed');

// The refusals: the same page with their codes (a failure, as before).
await page.goto(base + '/account/passkeys?msg=passkey-last-cannot-remove');
await each('notice-last');
await page.goto(base + '/account/passkeys?msg=passkey-not-found');
await each('notice-not-found');

console.log(metrics.join('\n'));
await browser.close();
