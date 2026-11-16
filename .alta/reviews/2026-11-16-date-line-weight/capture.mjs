// Capture walk for the design review of task dt8w (the date line at normal weight on three screens).
// Usage: node capture.mjs <base> <setup-token> <out> <database-file>
//   One fresh Release instance in Development (http://localhost:PORT, so WebAuthn accepts the
//   origin), started with HTTPS_PROXY=http://127.0.0.1:9. Setup goes through the real form; the
//   operator adds a passkey ("Laptop") on Credential with Chrome's virtual authenticator; the
//   Passkey connector gets open sign-up and a person signs up ("MacBook"), adds "Pixel 9" and signs
//   in with it. A link can only come from a real provider sign-in, so one user with a GitHub link
//   and a passkey is written into the SQLite file; the Users screen is then read through the browser.
//   Three shots at 1280, dark: credential, your-passkeys, users-card.
import { chromium } from '/tmp/ypr0-pw/node_modules/playwright/index.mjs';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';

const [base, setupToken, out, dbFile] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const credential = 'dt8w-capture-operator-credential';
const theme = 'dark';
const width = 1280;
const metrics = [];
const sql = (text) => execFileSync('sqlite3', [dbFile], { input: text }).toString();

const browser = await chromium.launch({ channel: 'chrome' });

async function withAuthenticator(context, page) {
  const cdp = await context.newCDPSession(page);
  await cdp.send('WebAuthn.enable');
  let authenticatorId = null;
  return async function fresh() {
    if (authenticatorId) await cdp.send('WebAuthn.removeVirtualAuthenticator', { authenticatorId });
    ({ authenticatorId } = await cdp.send('WebAuthn.addVirtualAuthenticator', {
      options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true,
                 isUserVerified: true, automaticPresenceSimulation: true },
    }));
  };
}

async function shot(context, page, name, path, rowSelector, dateSelector) {
  await context.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
  await page.setViewportSize({ width, height: 900 });
  await page.goto(base + path);
  await page.waitForLoadState('networkidle');
  const o = await page.evaluate(([rs, ds]) => {
    const rows = [...document.querySelectorAll(rs)].map((r) => {
      const d = r.querySelector(ds);
      const cs = d ? getComputedStyle(d) : null;
      return { text: d?.innerText ?? null, cls: d?.className ?? null,
               weight: cs?.fontWeight ?? null, size: cs?.fontSize ?? null };
    });
    return { sw: document.documentElement.scrollWidth, iw: window.innerWidth, rows };
  }, [rowSelector, dateSelector]);
  metrics.push(`${name}-${width}-${theme}: overflow=${o.sw > o.iw} (scrollWidth ${o.sw}, innerWidth ${o.iw})\n  ` +
               o.rows.map((r) => `"${r.text}" class="${r.cls}" weight=${r.weight} size=${r.size}`).join('\n  '));
  await page.screenshot({ path: `${out}/${name}-${width}-${theme}.png`, fullPage: true });
}

// The operator: setup, a passkey of their own, then the Passkey connector with open sign-up.
const opContext = await browser.newContext({ viewport: { width, height: 900 } });
const op = await opContext.newPage();
const opAuthenticator = await withAuthenticator(opContext, op);
await opAuthenticator();
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
await op.goto(base + '/admin/credential');
await op.locator('#name').fill('Laptop');
await op.locator('#current-add').fill(credential);
await op.getByRole('button', { name: 'Add a passkey' }).click();
await op.waitForURL(/msg=operator-passkey-added/, { timeout: 30000 });
console.log('operator passkey added: ' + op.url());

await op.goto(base + '/admin/connectors/passkey');
const pane = op.locator('form:has(input[name=enrolment])').first();
await pane.locator('input[name=enrolment][value=open]').check();
const shared = pane.locator('input[name=shared][type=checkbox]');
if (await shared.count()) await shared.check();
await pane.locator('button[type=submit]').last().click();
await op.waitForLoadState('networkidle');
console.log('passkey connector saved: ' + op.url());

// The person: sign up with one key, add a second, sign in again with the second.
const context = await browser.newContext({ viewport: { width, height: 900 } });
const page = await context.newPage();
const freshAuthenticator = await withAuthenticator(context, page);
await freshAuthenticator();
await page.goto(base + '/connect/passkey/start?returnUrl=%2Faccount%2Fpasskeys');
await page.fill('form[data-passkey-signup] [name=displayName]', 'Sam Okoro');
await page.fill('form[data-passkey-signup] [name=nickname]', 'MacBook');
await Promise.all([page.waitForURL((u) => !u.pathname.startsWith('/connect/passkey')),
                   page.click('form[data-passkey-signup] button[type=submit]')]);
await page.waitForLoadState('networkidle');
await freshAuthenticator();
await page.goto(base + '/account/passkeys');
await page.fill('form[data-passkey-add] [name=nickname]', 'Pixel 9');
await Promise.all([page.waitForURL(/msg=passkey-added/), page.click('form[data-passkey-add] button[type=submit]')]);
await context.clearCookies();
await page.goto(base + '/connect/passkey/start?returnUrl=%2Faccount%2Fpasskeys');
await Promise.all([page.waitForURL(/\/account\/passkeys/, { timeout: 30000 }),
                   page.getByRole('button', { name: 'Continue with your passkey' }).click()]);
console.log('person signed in with the passkey: ' + page.url());

// A user with a GitHub link and a passkey, so the Users card draws both lists.
sql(`PRAGMA foreign_keys = ON;
INSERT INTO users (id, display_name, email, email_verified, is_enabled, first_seen_utc, last_seen_utc)
  VALUES ('u-alice', 'Alice Moreau', 'alice@example.org', 1, 1, '2026-06-02T10:00:00Z', '2026-11-13T09:00:00Z');
INSERT INTO linked_accounts (provider_kind, provider_subject, user_id, asserted_email, asserted_email_verified, linked_utc, last_used_utc)
  VALUES ('github', 'gh-1001', 'u-alice', 'alice@example.org', 1, '2026-06-02T10:00:00Z', '2026-11-13T09:00:00Z');
INSERT INTO passkeys (id, user_id, credential_id, public_key, nickname, created_utc, last_used_utc)
  VALUES ('pk-alice-mac', 'u-alice', randomblob(16), randomblob(32), 'Alice''s MacBook', '2026-07-15T08:00:00Z', '2026-11-12T08:00:00Z');
`);

await shot(opContext, op, 'credential', '/admin/credential', '[data-passkey-row]', 'p.text-label-md');
await shot(context, page, 'your-passkeys', '/account/passkeys', '[data-passkey]', 'p.text-label-md');
await shot(opContext, op, 'users-card', '/admin/users?search=Alice',
           'article[data-user="u-alice"] li[data-user-link], article[data-user="u-alice"] li[data-passkey]',
           ':scope > span:nth-of-type(2)');

console.log(metrics.join('\n'));
await browser.close();
