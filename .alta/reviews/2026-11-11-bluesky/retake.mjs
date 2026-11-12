// Retake after the design-review changes (a4bd928). Usage: node retake.mjs <mode A|C> <base> <token> <out> [hostRules]
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
const [mode, base, setupToken, out, hostRules] = process.argv.slice(2);
const credential = 'a-very-good-credential-b5ky';
const browser = await chromium.launch({ channel: 'chrome', args: hostRules ? [`--host-resolver-rules=${hostRules}`] : [] });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, ignoreHTTPSErrors: true });
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
const paneUrl = base + '/admin/connectors/bluesky';
async function save(scope, servers) {
  await page.goto(paneUrl);
  await page.check(`input[name=scope][value=${scope}]`);
  await page.fill('[name=servers]', servers);
  await Promise.all([page.waitForResponse((r) => r.request().method() === 'POST'), page.locator('form[action="/admin/connectors/bluesky"] button[type=submit]').first().click()]);
  await page.waitForLoadState('networkidle');
}
async function shot(name, theme) {
  await context.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
  await page.goto(paneUrl);
  await page.waitForLoadState('networkidle');
  const o = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth]);
  console.log(name, theme, 'overflow=' + (o[0] > o[1]), o.join('/'), 'placeholder=' + (await page.locator('[name=servers]').getAttribute('placeholder')));
  await page.screenshot({ path: `${out}/${name}-1280-${theme}.png`, fullPage: true });
}
await save('any', '');
if (mode === 'C') await shot('connectors-any-https', 'dark');
else {
  await shot('connectors-any', 'dark');
  await save('listed', 'bsky.social, pds.example.org');
  await shot('connectors-list', 'light');
}
await browser.close();
