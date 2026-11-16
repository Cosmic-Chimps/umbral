// Capture walk for the design review of task ypr0 (Your passkeys: rows like the Credential screen's).
// Usage: node capture.mjs <base> <setup-token> <out>
//   One fresh Release instance in Development (http://localhost:PORT, so WebAuthn accepts the
//   origin), started with HTTPS_PROXY=http://127.0.0.1:9. Setup goes through the real form, the
//   Passkey connector is enabled with open sign-up through its pane, and the person signs up with
//   Chrome's virtual authenticator ("MacBook"), adds "Pixel 9" on a second one, signs out and
//   signs in with the Pixel 9: one key used, one never used.
import { chromium } from '/tmp/ypr0-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, setupToken, out] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const credential = 'ypr0-capture-operator-credential';
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

async function shot(name, width, theme) {
  await context.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
  await page.setViewportSize({ width, height: 900 });
  await page.goto(base + '/account/passkeys');
  await page.waitForLoadState('networkidle');
  const o = await page.evaluate(() => {
    const rows = [...document.querySelectorAll('[data-passkey]')].map((a) => ({
      dates: a.querySelector('p')?.innerText ?? null,
      datesClass: a.querySelector('p')?.className ?? null,
      remove: a.querySelector('button[type=submit]')?.className ?? null,
      confirm: a.querySelector('form')?.getAttribute('data-confirm') ?? null,
    }));
    return { sw: document.documentElement.scrollWidth, iw: window.innerWidth, rows };
  });
  metrics.push(`${name}-${width}-${theme}: overflow=${o.sw > o.iw} (scrollWidth ${o.sw}, innerWidth ${o.iw})\n  ` +
               o.rows.map((r) => `"${r.dates}" class="${r.datesClass}" remove="${r.remove}" confirm=${r.confirm !== null}`).join('\n  '));
  await page.screenshot({ path: `${out}/${name}-${width}-${theme}.png`, fullPage: true });
}

// A second key, on a second authenticator: never used.
console.log('added: ' + await addPasskey('Pixel 9'));

// Sign out, then sign in again with the Pixel 9 (the current authenticator): it is now used,
// the MacBook (the sign-up key) never is.
await context.clearCookies();
await page.goto(base + '/connect/passkey/start?returnUrl=%2Faccount%2Fpasskeys');
await Promise.all([page.waitForURL(/\/account\/passkeys/, { timeout: 30000 }),
                   page.getByRole('button', { name: 'Continue with your passkey' }).click()]);
await page.waitForLoadState('networkidle');
console.log('signed in with the passkey, now at ' + page.url());

for (const theme of ['dark', 'light']) for (const width of [1280, 360]) await shot('your-passkeys', width, theme);

console.log(metrics.join('\n'));
await browser.close();
