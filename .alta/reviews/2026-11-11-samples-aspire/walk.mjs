// Hand walk of the Aspire sample (task r5hd), half two: the contract's walk against the
// sample, signing in with a passkey on a virtual authenticator (Chrome's WebAuthn
// domain over CDP). Args: sample base URL, output folder for captures.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, out] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const browser = await chromium.launch({ channel: 'chrome' });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await context.newPage();
const cdp = await context.newCDPSession(page);
await cdp.send('WebAuthn.enable');
await cdp.send('WebAuthn.addVirtualAuthenticator', {
  options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true },
});

const h1 = () => page.locator('h1').first().innerText();
const check = (what, ok) => { console.log((ok ? 'ok   ' : 'FAIL ') + what); if (!ok) process.exitCode = 1; };

// The home page, signed out.
await page.goto(base + '/');
check('/ has h1 "' + await h1() + '"', (await h1()).length > 0);
check('/ has Public, Private, Sign in', await page.getByRole('link', { name: 'Public', exact: true }).count() === 1
  && await page.getByRole('link', { name: 'Private', exact: true }).count() === 1
  && await page.getByRole('link', { name: 'Sign in', exact: true }).count() === 1);
await page.screenshot({ path: out + '/home-signed-out.png' });

await page.goto(base + '/public');
check('/public h1 is Public', (await h1()) === 'Public');

// /private sends a signed-out visitor to Umbral.
await page.goto(base + '/private');
await page.locator('[data-screen="signin"]').waitFor();
check('/private sent to Umbral sign-in (' + new URL(page.url()).origin + ')', page.url().startsWith('http://localhost:18080/'));
const title = await h1();
console.log('     sign-in page heading: ' + title);
await page.screenshot({ path: out + '/umbral-signin.png' });

// Sign in with a passkey: create an account on the virtual authenticator.
await page.getByRole('link', { name: 'Sign in with a passkey' }).or(page.getByRole('button', { name: 'Sign in with a passkey' })).first().click();
await page.locator('[data-screen="passkey-signin"]').waitFor();
await page.screenshot({ path: out + '/umbral-passkey.png' });
// Open sign-up: the form on the passkey page creates the account and the passkey in one go.
await page.getByLabel('Your name').fill('Aspire Person');
await page.getByRole('button', { name: 'Create an account with a passkey' }).click();
await page.waitForURL(/\/private$/, { timeout: 30000 });
check('/private after the passkey sign-in, h1 is Private', (await h1()) === 'Private');
const rows = {};
for (const tr of await page.locator('tr').all()) {
  const th = await tr.locator('th').innerText();
  rows[th.trim()] = (await tr.locator('td').innerText()).trim();
}
console.log('     claims: ' + JSON.stringify(rows));
check('sub, name, idp present', !!rows.sub && rows.name === 'Aspire Person' && rows.idp === 'passkey');
check('no email rows', !('email' in rows) && !('email_verified' in rows));
await page.screenshot({ path: out + '/private-claims.png' });

// Sign out: the app's session, then Umbral's, then back on /.
await page.goto(base + '/');
await page.getByRole('button', { name: 'Sign out' }).click();
await page.waitForURL(base + '/');
check('/ shows Sign in again', await page.getByRole('link', { name: 'Sign in', exact: true }).count() === 1);
await page.goto(base + '/private');
await page.locator('[data-screen="signin"]').waitFor();
check('/private sends to Umbral again', true);
await browser.close();
