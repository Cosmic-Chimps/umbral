// Walks the built documentation site in a real browser and reports what the
// Content-Security-Policy refused, what the console said, and what was asked
// of another host. It ends with exit code 0 when there is nothing to report.
//
// In each engine, at 1280 and at 360 wide, in the dark and in the light theme
// (chosen with the theme select, against the opposite colour scheme of the
// system): every page of dist/, the menu and the "On this page" dropdown at
// 360, the copy button of a code block (Chrome), then a search, its first
// result, and a page that does not exist.
//
//   npm i playwright-core && npx playwright-core install firefox webkit
//   (cd site && npm run build && npx astro preview --port 4399 --host 127.0.0.1)
//   node walk.mjs http://127.0.0.1:4399/umbral/ <path to site/dist> [chrome|firefox|webkit]
//
// Options: --shots <dir> keeps captures; --brief prints the summary alone;
// --no-policy serves the same pages without the policy element, to tell what
// the policy causes from what it does not. The findings are also written to
// findings-<engine>.json in the current directory.
import { readdirSync, statSync, mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { chromium, firefox, webkit } from 'playwright-core';

const [base, dist, engine = 'chrome'] = process.argv.slice(2);
const shotsAt = process.argv.indexOf('--shots');
const shots = shotsAt > 0 ? process.argv[shotsAt + 1] : null;
if (shots) mkdirSync(shots, { recursive: true });
const origin = new URL(base).origin;
const QUERY = 'redirect_uri_mismatch';

function* htmlFiles(dir) {
  for (const name of readdirSync(dir)) {
    const full = path.join(dir, name);
    if (statSync(full).isDirectory()) yield* htmlFiles(full);
    else if (name.endsWith('.html')) yield full;
  }
}
const pages = [...htmlFiles(dist)]
  .map((f) => path.relative(dist, f).split(path.sep).join('/'))
  .map((rel) => (rel === 'index.html' ? '' : rel.endsWith('/index.html') ? rel.slice(0, -'index.html'.length) : rel))
  .sort();

const launcher = { chrome: () => chromium.launch({ channel: 'chrome' }), firefox: () => firefox.launch(), webkit: () => webkit.launch() }[engine];
const browser = await launcher();

const findings = [];
const stats = { engine, version: browser.version(), pages: pages.length, visits: 0, searches: 0, menus: 0, themeSwitches: 0, tocs: 0, copies: 0 };
let expecting404 = false; // true while the walk asks for the page that does not exist
const note = (where, kind, text) => findings.push({ where, kind, text });

async function newPage(context, label) {
  const page = await context.newPage();
  if (process.argv.includes('--no-policy')) {
    await page.route((u) => u.pathname.endsWith('/') || u.pathname.endsWith('.html'), async (route) => {
      const response = await route.fetch();
      const body = (await response.text()).replace(/<meta http-equiv="Content-Security-Policy"[^>]*>/, '');
      await route.fulfill({ response, body });
    });
  }
  page.on('console', (m) => {
    const type = m.type();
    const text = m.text();
    if (expecting404 && /404/.test(text)) return;
    if (type === 'error' || type === 'warning' || /content.security.policy/i.test(text)) note(`${label} ${page.url()}`, `console.${type}`, text);
  });
  page.on('pageerror', (e) => note(`${label} ${page.url()}`, 'pageerror', String(e)));
  page.on('request', (r) => {
    const u = r.url();
    if (!u.startsWith(origin) && !u.startsWith('data:') && !u.startsWith('blob:') && !u.startsWith('about:')) note(`${label} ${page.url()}`, 'other-host-request', u);
  });
  page.on('requestfailed', (r) => note(`${label} ${page.url()}`, 'requestfailed', `${r.url()} ${r.failure()?.errorText ?? ''}`));
  page.on('response', (r) => {
    if (r.status() >= 400 && !r.url().endsWith('/404.html') && !/\/no-such-page\/$/.test(r.url())) note(`${label} ${page.url()}`, 'http', `${r.status()} ${r.url()}`);
  });
  await page.exposeFunction('__cspViolation', (v) => note(`${label} ${v.documentURI}`, 'csp-violation', `${v.effectiveDirective} blocked ${v.blockedURI || '(inline)'} ${v.sourceFile ?? ''}:${v.lineNumber ?? ''} sample=${JSON.stringify(v.sample ?? '')}`));
  await page.addInitScript(() => {
    document.addEventListener('securitypolicyviolation', (e) => {
      window.__cspViolation({
        documentURI: e.documentURI, effectiveDirective: e.effectiveDirective, blockedURI: e.blockedURI,
        sourceFile: e.sourceFile, lineNumber: e.lineNumber, sample: e.sample,
      });
    });
  });
  return page;
}

async function search(page, label) {
  await page.locator('site-search button[data-open-modal]').click();
  const dialog = page.locator('site-search dialog');
  await dialog.waitFor({ state: 'visible' });
  const input = dialog.locator('input').first();
  await input.waitFor({ state: 'visible', timeout: 15000 });
  await input.fill(QUERY);
  const results = dialog.locator('a[href*="/umbral/"]');
  try {
    await results.first().waitFor({ state: 'visible', timeout: 15000 });
  } catch {
    note(label, 'search', `no result for ${QUERY}`);
    return;
  }
  const count = await results.count();
  const first = await results.first().getAttribute('href');
  stats.searches++;
  stats.lastSearch = `${count} links, first ${first}`;
  if (shots) await page.screenshot({ path: path.join(shots, `search-${label.replace(/\s+/g, '-')}.png`) });
  await results.first().click();
  await page.waitForLoadState('networkidle');
  if (!new URL(page.url()).pathname.startsWith(new URL(first, origin).pathname)) note(label, 'search', `the first result did not open: at ${page.url()}`);
}

for (const width of [1280, 360]) {
  for (const theme of ['dark', 'light']) {
    const label = `${engine} ${width} ${theme}`;
    // The opposite colour scheme, so the theme comes from the select and its stored choice.
    const context = await browser.newContext({
      viewport: { width, height: 800 },
      colorScheme: theme === 'dark' ? 'light' : 'dark',
      ...(engine === 'chrome' ? { permissions: ['clipboard-read', 'clipboard-write'] } : {}),
    });
    const page = await newPage(context, label);

    await page.goto(base, { waitUntil: 'networkidle' });
    if (width === 360) {
      await page.locator('button.sl-menu-button').click();
      await page.locator('#starlight__sidebar starlight-theme-select select').selectOption(theme);
      await page.locator('button.sl-menu-button').click();
    } else {
      await page.locator('starlight-theme-select select').first().selectOption(theme);
    }
    stats.themeSwitches++;
    const applied = await page.evaluate(() => [document.documentElement.dataset.theme, localStorage.getItem('starlight-theme')]);
    if (applied[0] !== theme || applied[1] !== theme) note(label, 'theme', `select chose ${theme}, page has ${applied}`);

    for (const rel of pages) {
      const url = base + rel;
      const response = await page.goto(url, { waitUntil: 'networkidle' });
      stats.visits++;
      if (!response || (![200, 304].includes(response.status()) && rel !== '404.html')) note(label, 'http', `${response?.status()} ${url}`);
      const state = await page.evaluate(() => ({
        theme: document.documentElement.dataset.theme,
        policy: document.querySelector('meta[http-equiv="Content-Security-Policy"]')?.content ?? null,
        beforeAnyScript: (() => {
          const meta = document.querySelector('meta[http-equiv="Content-Security-Policy"]');
          const first = document.querySelector('script, link[rel~="stylesheet"], style');
          return !!meta && !!first && !!(meta.compareDocumentPosition(first) & Node.DOCUMENT_POSITION_FOLLOWING);
        })(),
        fonts: [...document.fonts].filter((f) => f.status === 'loaded').length,
        searchEnabled: !document.querySelector('site-search button[data-open-modal]')?.disabled,
      }));
      if (state.theme !== theme) note(label, 'theme', `${url} is ${state.theme}`);
      if (!state.policy && !process.argv.includes('--no-policy')) note(label, 'policy', `${url} has no policy element`);
      if (!state.beforeAnyScript && !process.argv.includes('--no-policy')) note(label, 'policy', `${url}: the policy element is not before the first script or stylesheet`);
      if (!state.searchEnabled) note(label, 'search', `${url}: the search button stayed disabled`);
      stats.policy = state.policy;
      await page.evaluate(() => document.fonts.ready);
      const loaded = await page.evaluate(() => [...document.fonts].filter((f) => f.status === 'loaded').length);
      if (loaded === 0) note(label, 'fonts', `${url}: no font face loaded`);

      if (width === 360 && rel !== '404.html') {
        const button = page.locator('button.sl-menu-button');
        if (await button.count()) {
          await button.click();
          const open = await page.evaluate(() => document.getElementById('starlight__sidebar').matches(':popover-open'));
          const visible = await page.locator('#starlight__sidebar a').first().isVisible().catch(() => false);
          if (!open || !visible) note(label, 'menu', `${url}: the menu did not open (expanded=${open}, sidebar link visible=${visible})`);
          else stats.menus++;
          if (shots && rel === '') await page.screenshot({ path: path.join(shots, `menu-${width}-${theme}.png`) });
          await button.click();
          if (await page.evaluate(() => document.getElementById('starlight__sidebar').matches(':popover-open'))) note(label, 'menu', `${url}: the menu did not close`);
        }
        const toc = page.locator('mobile-starlight-toc summary');
        if (await toc.count()) {
          await toc.click();
          if (await page.locator('mobile-starlight-toc .dropdown a').first().isVisible().catch(() => false)) stats.tocs++;
          else note(label, 'toc', `${url}: the "On this page" dropdown did not open`);
          await toc.click();
        }
      }
      const copy = page.locator('.expressive-code .copy button').first();
      if (engine === 'chrome' && (await copy.count()) && (await copy.isVisible().catch(() => false))) {
        await copy.click();
        stats.copies++;
      }
      if (shots && (rel === '' || rel === 'run/https/')) await page.screenshot({ path: path.join(shots, `${rel === '' ? 'start' : 'run-https'}-${width}-${theme}.png`) });
    }

    await page.goto(base, { waitUntil: 'networkidle' });
    await search(page, label);
    expecting404 = true;
    const missing = await page.goto(`${base}no-such-page/`, { waitUntil: 'networkidle' });
    if (missing.status() !== 404) note(label, 'http', `the page that does not exist answered ${missing.status()}`);
    if (!(await page.evaluate(() => !!document.querySelector('meta[http-equiv="Content-Security-Policy"]')))) { if (!process.argv.includes('--no-policy')) note(label, 'policy', 'the 404 page has no policy element'); }
    await context.close();
    expecting404 = false;
  }
}
await browser.close();

writeFileSync(`findings-${engine}${process.argv.includes('--no-policy') ? '-nopolicy' : ''}.json`, JSON.stringify(findings, null, 1));
const byKind = {};
for (const f of findings) byKind[f.kind] = (byKind[f.kind] ?? 0) + 1;
const distinct = new Map();
for (const f of findings) {
  const key = `${f.kind}: ${f.text.replace(/http:\/\/127\.0\.0\.1:\d+/g, '').slice(0, 260)}`;
  distinct.set(key, (distinct.get(key) ?? 0) + 1);
}
const directives = {};
for (const f of findings) {
  if (f.kind !== 'csp-violation') continue;
  const [directive, , blocked] = f.text.split(' ');
  const key = `${directive} ${blocked.startsWith('http') ? blocked.replace(/http:\/\/127\.0\.0\.1:\d+/, '') : blocked}`;
  directives[key] = (directives[key] ?? 0) + 1;
}
console.log(JSON.stringify({ stats, byKind, directives }, null, 1));
if (process.argv.includes('--brief')) process.exit(findings.length ? 1 : 0);
for (const [k, n] of [...distinct].sort((a, b) => b[1] - a[1]).slice(0, 40)) console.log(`${n}x ${k}`);
process.exit(findings.length ? 1 : 0);
