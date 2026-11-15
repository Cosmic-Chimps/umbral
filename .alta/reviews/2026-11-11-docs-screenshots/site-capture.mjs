// Captures the built documentation site's pages that show a screenshot, in the dark and the
// light system theme, and checks each picture: loaded, the file of the theme, no console error
// (a Content-Security-Policy violation is one).
// Usage: node site-capture.mjs <base, e.g. http://localhost:4393/umbral> <out folder>
//   after `npm run build` and `npx astro preview` in site/. Playwright from /tmp/r3du-pw.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';

const [base, out] = process.argv.slice(2);
const pages = ['', 'quick-start/', 'look/', 'branding/', 'connectors/passkey/', 'connectors/fediverse/', 'run/operator-passkeys/'];
const browser = await chromium.launch({ channel: 'chrome' });
let bad = 0;
for (const theme of ['dark', 'light']) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: theme });
  const page = await context.newPage();
  const errors = [];
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('requestfailed', (r) => errors.push(`failed ${r.url()}`));
  for (const route of pages) {
    await page.goto(`${base}/${route}`, { waitUntil: 'networkidle' });
    const pictures = await page.locator('.sl-markdown-content picture img').evaluateAll((imgs) =>
      imgs.map((img) => ({ src: img.currentSrc.replace(/^.*\/images\//, ''), loaded: img.complete && img.naturalWidth > 0, alt: img.alt.length })));
    const hosts = await page.evaluate(() => [...new Set(performance.getEntriesByType('resource').map((e) => new URL(e.name).host))]);
    for (const p of pictures) {
      const ok = p.loaded && p.src.endsWith(`-${theme}.png`) && p.alt > 0;
      if (!ok) bad++;
      console.log(`${ok ? 'ok  ' : 'FAIL'} ${theme} /${route} ${p.src} loaded=${p.loaded} alt=${p.alt} chars`);
    }
    if (pictures.length === 0) { bad++; console.log(`FAIL ${theme} /${route} no picture`); }
    if (hosts.length !== 1) { bad++; console.log(`FAIL ${theme} /${route} hosts: ${hosts.join(', ')}`); }
    await page.screenshot({ path: `${out}/${(route || 'index/').replaceAll('/', '-').replace(/-$/, '')}-${theme}.png`, fullPage: true });
  }
  for (const e of errors) { bad++; console.log(`FAIL ${theme} console: ${e}`); }
  await context.close();
}
await browser.close();
console.log(bad === 0 ? 'all pictures loaded from the site, in the theme of the system, no console error' : `${bad} problem(s)`);
process.exit(bad === 0 ? 0 : 1);
