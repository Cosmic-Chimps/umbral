// Capture walk for the design review of task k3yi (the passkey step's Continue button wears the
// Key icon, as the picker's passkey button does).
// Usage: node capture.mjs <base> <setup-token> <out>
//   One fresh Release instance in Development (http://localhost:PORT), started with
//   HTTPS_PROXY=http://127.0.0.1:9. Setup goes through the real form, the Passkey connector is
//   enabled (invitations only, as in the docs screenshots) through its pane. Each shot records the
//   start of the button's icon path, to compare with Theme.Key and Theme.Link.
import { chromium } from '/tmp/k3yi-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, setupToken, out] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const credential = 'k3yi-capture-operator-credential';
const metrics = [];

const browser = await chromium.launch({ channel: 'chrome' });

// The operator: setup, the Passkey connector, one app.
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
await op.getByLabel('Invitations only').check();
await op.getByRole('button', { name: 'Enable passkeys' }).click();
await op.waitForURL(/\/admin\/connectors\/passkey\?msg=/);
console.log('passkey connector saved: ' + op.url());

// The visitor: the passkey step, reached from its start address.
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await context.newPage();
await page.goto(base + '/connect/passkey/start?returnUrl=%2Faccount%2Fpasskeys');
await page.locator('[data-screen="passkey-signin"]').waitFor();

async function shot(name, width, theme) {
  await context.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
  await page.setViewportSize({ width, height: 900 });
  await page.reload();
  await page.waitForLoadState('networkidle');
  const o = await page.evaluate(() => {
    const b = document.querySelector('button[data-passkey-signin]');
    return { sw: document.documentElement.scrollWidth, iw: window.innerWidth,
             text: b?.innerText.trim() ?? null, svg: b?.querySelector('svg path')?.getAttribute('d')?.slice(0, 40) ?? null };
  });
  metrics.push(`${name}-${width}-${theme}: overflow=${o.sw > o.iw} (scrollWidth ${o.sw}, innerWidth ${o.iw}); ` +
               `button="${o.text}" icon path starts "${o.svg}"`);
  await page.screenshot({ path: `${out}/${name}-${width}-${theme}.png`, fullPage: true });
}

for (const theme of ['dark', 'light']) for (const width of [1280, 360]) await shot('passkey-step', width, theme);

console.log(metrics.join('\n'));
await browser.close();
