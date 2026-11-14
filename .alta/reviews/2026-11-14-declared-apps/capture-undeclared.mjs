// Captures of the panes of an app and of the connector that are no longer declared (spec 019 D3).
// The instance is a scratch copy of the walk's volume started with no UMBRAL__DECLARATION.
// Usage: OPERATOR_CREDENTIAL=... node capture-undeclared.mjs <umbral base> <out dir>
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
const [base, out] = process.argv.slice(2);
const credential = process.env.OPERATOR_CREDENTIAL;
const browser = await chromium.launch({ channel: 'chrome' });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await context.newPage();
await page.goto(base + '/admin/signin');
await page.fill('[name=credential]', credential);
await page.press('[name=credential]', 'Enter');
await page.waitForLoadState('networkidle');
console.log('signed in; at ' + page.url());
for (const [state, url] of [['app-undeclared-pane', '/admin/apps/sample'], ['passkey-undeclared-pane', '/admin/connectors/passkey'], ['apps-list-undeclared', '/admin/apps']]) {
  for (const theme of ['dark', 'light']) {
    await context.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
    for (const width of [1280, 360]) {
      await page.setViewportSize({ width, height: 900 });
      await page.goto(base + url);
      await page.waitForLoadState('networkidle');
      const o = await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, iw: window.innerWidth, declared: document.body.innerText.includes('Declared') }));
      console.log(`${state}-${width}-${theme}: overflow=${o.sw > o.iw} word "Declared" on page: ${o.declared}`);
      await page.screenshot({ path: `${out}/${state}-${width}-${theme}.png`, fullPage: true });
    }
  }
}
await browser.close();
