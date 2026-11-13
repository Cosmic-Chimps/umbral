// Hand walk of the Aspire sample (task r5hd), half one: what the README's "What to
// create in Umbral" asks, done through the real admin screens with Playwright.
// Args: Umbral base URL, sample port, setup token, output file for the credentials
// (written 0600; never printed).
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, samplePort, setupToken, outFile] = process.argv.slice(2);
const credential = process.env.OPERATOR_CREDENTIAL;
if (!base || !samplePort || !setupToken || !outFile || !credential) {
  console.error('usage: setup.mjs <umbral base> <sample port> <setup token> <out file>, with OPERATOR_CREDENTIAL set');
  process.exit(2);
}

const browser = await chromium.launch({ channel: 'chrome' });
const page = await browser.newPage();

// 1. Claim: the setup page with the token from the console logs.
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
console.log('claimed; now at ' + page.url());

// 2. Connectors: Passkey, "Anyone can sign up", Enable passkeys.
await page.goto(base + '/admin/connectors/passkey');
await page.getByLabel('Anyone can sign up').check();
await page.getByRole('button', { name: 'Enable passkeys' }).click();
await page.waitForLoadState('networkidle');
console.log('passkey connector: ' + page.url());

// 3. Apps: Create app, Web application, the sample's two URIs.
const origin = 'http://localhost:' + samplePort;
await page.goto(base + '/admin/apps/new');
await page.getByLabel('Name', { exact: true }).fill('Aspire sample');
await page.getByLabel('Redirect URI', { exact: true }).fill(origin + '/signin-oidc');
await page.getByLabel('Post-logout redirect URI (optional)', { exact: true }).fill(origin + '/signout-callback-oidc');
await page.getByLabel('Web application').check();
await page.getByRole('button', { name: 'Create app' }).click();
await page.waitForURL(/\/admin\/apps\/[^/?#]+/);
const clientId = await page.locator('input[id^="client-id-"]').inputValue();
const clientSecret = await page.locator('[data-secret-once] input#revealed-secret').inputValue();
if (!clientId || !clientSecret) throw new Error('no client id or secret on the app page');
fs.writeFileSync(outFile, 'UMBRAL_CLIENT_ID=' + clientId + '\nUMBRAL_CLIENT_SECRET=' + clientSecret + '\n', { mode: 0o600 });
console.log('app created: ' + page.url() + ' (credentials in ' + outFile + ')');
await browser.close();
