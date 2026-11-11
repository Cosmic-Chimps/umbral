// Probes the policy of the built documentation site at run time, in one engine:
// a fetch, an image and a script from another origin, eval, a <base> element and
// WebAssembly on the page; a fetch and WebAssembly in a worker; and a search with
// Pagefind's worker refused, which makes it fall back to the page.
//
// It needs the preview of the site (see walk.mjs) and any server on another
// port to stand for "another host":  python3 -m http.server 4398 --bind 127.0.0.1
//
//   node probe.mjs http://127.0.0.1:4399/umbral/ [chrome|firefox|webkit]
import { chromium, firefox, webkit } from 'playwright-core';
const [base, engine = 'chrome'] = process.argv.slice(2);
const OTHER = 'http://127.0.0.1:4398/package.json';
const browser = await { chrome: () => chromium.launch({ channel: 'chrome' }), firefox: () => firefox.launch(), webkit: () => webkit.launch() }[engine]();
const out = { engine, version: browser.version() };

{
  const page = await (await browser.newContext()).newPage();
  const violations = [];
  await page.exposeFunction('__v', (v) => violations.push(v));
  await page.addInitScript(() => document.addEventListener('securitypolicyviolation', (e) => window.__v(`${e.effectiveDirective} ${e.blockedURI}`)));
  await page.route('**/umbral/__probe-worker.js', (r) => r.fulfill({ contentType: 'text/javascript', body: `onmessage = async (e) => { const f = await fetch(e.data, { mode: 'no-cors' }).then(() => 'fetched', (x) => 'refused: ' + x); const w = await WebAssembly.compile(new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0])).then(() => 'compiled', (x) => 'refused: ' + String(x).slice(0, 60)); postMessage('fetch ' + f + '; wasm ' + w); };` }));
  await page.goto(base, { waitUntil: 'load' });
  out.policy = await page.evaluate(() => document.querySelector('meta[http-equiv="Content-Security-Policy"]')?.content);
  out.pageFetch = await page.evaluate((u) => fetch(u, { mode: 'no-cors' }).then(() => 'fetched', (x) => 'refused: ' + x), OTHER);
  out.pageImage = await page.evaluate((u) => new Promise((done) => { const i = new Image(); i.onload = () => done('loaded'); i.onerror = () => done('refused'); i.src = u; }), 'http://127.0.0.1:4398/x.png');
  out.pageScript = await page.evaluate((u) => new Promise((done) => { const s = document.createElement('script'); s.onload = () => done('loaded'); s.onerror = () => done('refused'); s.src = u; document.head.append(s); }), 'http://127.0.0.1:4398/walk.mjs');
  // Through an inline script, not through the debugger, which is allowed to evaluate strings.
  out.pageEval = await page.evaluate(() => { const s = document.createElement('script'); s.textContent = "setTimeout(() => { try { window.__ev = String(window['ev' + 'al']('1+1')); } catch (x) { window.__ev = 'refused: ' + String(x).slice(0, 90); } }, 0)"; document.head.append(s); });
  await page.waitForFunction(() => window.__ev !== undefined, null, { timeout: 5000 }).catch(() => {});
  out.pageEval = await page.evaluate(() => window.__ev ?? 'the inline script did not run');
  out.baseElement = await page.evaluate(() => { const b = document.createElement('base'); b.href = 'http://127.0.0.1:4398/'; document.head.prepend(b); const got = document.baseURI; b.remove(); return got; });
  out.pageWasm = await page.evaluate(() => WebAssembly.compile(new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0])).then(() => 'compiled', (x) => 'refused: ' + String(x).slice(0, 90)));
  out.worker = await page.evaluate((u) => new Promise((done) => { const w = new Worker('/umbral/__probe-worker.js'); w.onmessage = (e) => done(e.data); w.onerror = (e) => done('worker error ' + e.message); w.postMessage(u); }), OTHER);
  out.violations = violations;
}
{
  // Search with Pagefind's worker refused: it falls back to the main thread.
  const page = await (await browser.newContext()).newPage();
  const messages = [];
  page.on('console', (m) => messages.push(`${m.type()}: ${m.text().slice(0, 200)}`));
  page.on('pageerror', (e) => messages.push(`pageerror: ${String(e).slice(0, 200)}`));
  await page.route('**/pagefind/pagefind-worker.js', (r) => r.abort());
  await page.goto(base, { waitUntil: 'load' });
  await page.locator('site-search button[data-open-modal]').click();
  const dialog = page.locator('site-search dialog');
  await dialog.locator('input').first().fill('redirect_uri_mismatch');
  const results = dialog.locator('a[href*="/umbral/"]');
  out.searchWithoutWorker = await results.first().waitFor({ state: 'visible', timeout: 10000 }).then(async () => `${await results.count()} links`, () => 'no result');
  out.searchWithoutWorkerConsole = messages;
}
await browser.close();
console.log(JSON.stringify(out, null, 1));
