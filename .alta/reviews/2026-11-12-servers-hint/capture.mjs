// Capture walk for the design review of task p1ch (the Servers field: the example in the hint).
// Usage: node capture.mjs <base> <setup-token> <out>
// The instance runs with the development exception (http://127.0.0.1:PORT), so both panes show the
// development note in place of the callback or metadata address (expected).
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, setupToken, out] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const credential = 'a-very-good-credential-p1ch';
const themes = ['dark', 'light'];
const widths = [1280, 360];
const metrics = [];

const browser = await chromium.launch({ channel: 'chrome' });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
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

// Enable a connector with Any server through its own form, as the operator would.
async function enableAny(slug) {
  const url = `${base}/admin/connectors/${slug}`;
  await page.goto(url);
  await page.check('input[name=scope][value=any]');
  const save = page.locator(`form[action="/admin/connectors/${slug}"] button[type=submit]`).first();
  const [response] = await Promise.all([page.waitForResponse((r) => r.request().method() === 'POST'), save.click()]);
  await page.waitForLoadState('networkidle');
  return `${response.status()} -> ${page.url()}`;
}

async function shots(slug) {
  for (const theme of themes) {
    await context.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
    for (const width of widths) {
      await page.setViewportSize({ width, height: 900 });
      await page.goto(`${base}/admin/connectors/${slug}`);
      await page.waitForLoadState('networkidle');
      const o = await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, iw: window.innerWidth }));
      const name = `${slug}-connectors-any-${width}-${theme}`;
      metrics.push(`${name}: overflow=${o.sw > o.iw} (scrollWidth ${o.sw}, innerWidth ${o.iw})`);
      await page.screenshot({ path: `${out}/${name}.png`, fullPage: true });
    }
  }
}

for (const slug of ['fediverse', 'bluesky']) {
  console.log(`${slug} enable (any) ->`, await enableAny(slug));
  await page.goto(`${base}/admin/connectors/${slug}`);
  const input = page.locator('input[data-instance-list]');
  console.log(`${slug} servers value: ${JSON.stringify(await input.inputValue())}, placeholder: ${JSON.stringify(await input.getAttribute('placeholder'))}`);
  const hintId = (await input.getAttribute('aria-describedby')) ?? '';
  const hint = hintId ? await page.locator(`[id="${hintId.split(' ')[0]}"]`).innerText().catch(() => '(not found)') : '(no aria-describedby)';
  console.log(`${slug} hint: ${hint}`);
  console.log(`${slug} any checked: ${await page.locator('input[name=scope][value=any]').isChecked()}`);
  await shots(slug);
}

console.log(metrics.join('\n'));
await browser.close();
