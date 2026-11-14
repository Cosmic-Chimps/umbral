// Walk of the Aspire sample against a declared Umbral (spec 019, T005): open /private on the sample,
// land on Umbral's sign-in, create an account with a passkey on a virtual authenticator (Chrome's
// WebAuthn domain over CDP), reach /private. Args: sample base URL, person name, output screenshot.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';

const [base, name, shot] = process.argv.slice(2);
const browser = await chromium.launch({ channel: 'chrome' });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await context.newPage();
const cdp = await context.newCDPSession(page);
await cdp.send('WebAuthn.enable');
await cdp.send('WebAuthn.addVirtualAuthenticator', {
  options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true },
});
const h1 = () => page.locator('h1').first().innerText();
await page.goto(base + '/private');
await page.locator('[data-screen="signin"]').waitFor();
console.log('sent to ' + new URL(page.url()).origin);
await page.getByRole('link', { name: 'Sign in with a passkey' }).or(page.getByRole('button', { name: 'Sign in with a passkey' })).first().click();
await page.locator('[data-screen="passkey-signin"]').waitFor();
await page.getByLabel('Your name').fill(name);
await page.getByRole('button', { name: 'Create an account with a passkey' }).click();
await page.waitForURL(/\/private$/, { timeout: 30000 });
const ok = (await h1()) === 'Private';
const rows = {};
for (const tr of await page.locator('tr').all()) {
  rows[(await tr.locator('th').innerText()).trim()] = (await tr.locator('td').innerText()).trim();
}
console.log((ok ? 'ok   ' : 'FAIL ') + '/private h1=' + (await h1()) + ' name=' + rows.name + ' idp=' + rows.idp);
await page.screenshot({ path: shot });
await browser.close();
process.exit(ok && rows.name === name ? 0 : 1);
