// Capture walk for the design review of task l1st (the home page's provider list).
// Usage: node capture.mjs <base> <out>
//   One fresh instance (http://127.0.0.1:PORT, development exception). The home page is static
//   and needs no setup: it is the same for every visitor.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, out] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const themes = ['dark', 'light'];
const widths = [1280, 360];
const metrics = [];

const browser = await chromium.launch({ channel: 'chrome' });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await context.newPage();

for (const theme of themes) {
  await context.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
  for (const width of widths) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(base + '/');
    await page.waitForLoadState('networkidle');
    const o = await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, iw: window.innerWidth }));
    const lead = await page.locator('main p').first().innerText();
    metrics.push(`home-${width}-${theme}: overflow=${o.sw > o.iw} (scrollWidth ${o.sw}, innerWidth ${o.iw})`);
    if (theme === 'dark' && width === 1280) console.log('lead: ' + lead);
    await page.screenshot({ path: `${out}/home-${width}-${theme}.png`, fullPage: true });
  }
}

console.log(metrics.join('\n'));
await browser.close();
