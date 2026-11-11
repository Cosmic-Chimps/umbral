// Proves, after every build, that the built site asks no other host for anything.
//
// Umbral's own UI loads nothing from third parties, and its documentation
// follows suit: no analytics, no CDN script, no external font, no remote image.
// Starlight's defaults already behave that way. This script is what keeps it
// true: a dependency update, a stylesheet edit or a Markdown page that starts
// reaching for another host fails the build here, instead of reaching a reader.
//
// Own host is the host of `site` in site.config.mjs. A link a reader may follow
// (the href of an <a>) or text may name any host; nothing the browser fetches on
// its own may.

import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { site } from '../site.config.mjs';

const DIST = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', 'dist');
const OWN_HOST = new URL(site).host;

// URL strings that appear in the built JavaScript and are not requests. Each was
// found by reading what the build produced; anything not listed fails the build,
// so a dependency that starts calling home is noticed. A new entry needs a reason.
const ALLOWED_JS_URLS = new Map([
  ['http://www.w3.org/2000/svg', 'the SVG XML namespace, the argument of createElementNS, never fetched'],
  ['https://p', 'a dummy origin that new URL(path, base) needs to resolve a pathname, never fetched'],
  ['https://example.com', 'a dummy origin in pagefind to parse a relative URL, never fetched'],
  ['https://markjs.io/', 'the homepage in the licence comment of mark.js, in pagefind-highlight.js'],
  ['https://git.io/vwTVl', 'a link in a comment of mark.js, in pagefind-highlight.js'],
  // The credits of the translations of Pagefind's search interface (a string beside
  // each language), in ui-core and pagefind-ui and pagefind-component-ui.
  ['https://github.com/pvillaverde', 'translator credit in a Pagefind UI language string'],
  ['https://github.com/dallyh', 'translator credit in a Pagefind UI language string'],
  ['https://github.com/diomed', 'translator credit in a Pagefind UI language string'],
  ['https://github.com/seokho-son', 'translator credit in a Pagefind UI language string'],
  ['https://yekta.dev', 'translator credit in a Pagefind UI language string'],
  ['https://goudarzjafari.com', 'translator credit in a Pagefind UI language string'],
  ['https://harrymkt.github.io', 'translator credit in a Pagefind UI language string'],
]);

const problems = [];
const problem = (file, message) => problems.push(`${path.relative(DIST, file)}: ${message}`);

function walk(dir, found = []) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full, found);
    else found.push(full);
  }
  return found;
}

/** The host of a URL that is not the site's own, or null when it is relative, own, or not a URL. */
function otherHost(value) {
  const v = value.trim();
  if (!/^(https?:)?\/\//i.test(v)) return null;
  try {
    const host = new URL(v, 'https://relative.invalid').host;
    return host === OWN_HOST ? null : host;
  } catch {
    return v;
  }
}

/** The external URLs a piece of CSS fetches: url(...) and @import. data: and relative URLs are fine. */
function cssUrls(css) {
  const urls = [];
  for (const m of css.matchAll(/url\(\s*(['"]?)(.*?)\1\s*\)/gi)) urls.push(m[2]);
  for (const m of css.matchAll(/@import\s+(?:url\(\s*)?['"]?([^'")\s;]+)/gi)) urls.push(m[1]);
  return urls.filter((u) => otherHost(u) !== null);
}

const TAG = /<([a-zA-Z][\w:-]*)((?:\s+[^\s"'<>\/=]+(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s"'<>=`]+))?)*)\s*\/?>/g;
const ATTR = /([^\s"'<>\/=]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'<>=`]+)))?/g;
// Attributes the browser fetches from, on whatever element carries them.
const FETCHED = new Set(['src', 'srcset', 'poster', 'data', 'action', 'xlink:href']);

const externalLinks = new Map(); // host -> count, for <a href>

function checkHtml(file, html) {
  const scripts = [];
  const styles = [];
  const rest = html
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi, (_, attrs, body) => {
      scripts.push({ attrs, body });
      return `<script${attrs}></script>`;
    })
    .replace(/<style\b[^>]*>([\s\S]*?)<\/style>/gi, (all, body) => {
      styles.push(body);
      return '<style></style>';
    });

  for (const { body } of scripts) {
    for (const m of body.matchAll(/https?:\/\/[^\s"'`)<>\\]+/g)) {
      if (m[0].startsWith('http://www.w3.org/') || otherHost(m[0]) === null) continue;
      problem(file, `an inline <script> holds the URL ${m[0]}`);
    }
  }
  for (const css of styles) {
    for (const u of cssUrls(css)) problem(file, `a <style> element fetches ${u}`);
  }

  for (const tag of rest.matchAll(TAG)) {
    const name = tag[1].toLowerCase();
    const attrs = new Map();
    for (const a of tag[2].matchAll(ATTR)) attrs.set(a[1].toLowerCase(), a[2] ?? a[3] ?? a[4] ?? '');
    for (const [attr, value] of attrs) {
      if (attr === 'xmlns' || attr.startsWith('xmlns:')) continue;
      if (attr === 'href') {
        const host = otherHost(value);
        if (host === null) continue;
        if (name === 'a') externalLinks.set(host, (externalLinks.get(host) ?? 0) + 1);
        else problem(file, `<${name} href> points at ${value}`);
      } else if (attr === 'srcset') {
        for (const candidate of value.split(',')) {
          const url = candidate.trim().split(/\s+/)[0];
          if (otherHost(url) !== null) problem(file, `<${name} srcset> holds ${url}`);
        }
      } else if (FETCHED.has(attr)) {
        if (otherHost(value) !== null) problem(file, `<${name} ${attr}> points at ${value}`);
      } else if (attr === 'style') {
        for (const u of cssUrls(value)) problem(file, `an inline style on <${name}> fetches ${u}`);
      } else if (name === 'meta' && attr === 'content' && /^refresh$/i.test(attrs.get('http-equiv') ?? '')) {
        const target = /url\s*=\s*['"]?([^'";\s]+)/i.exec(value)?.[1];
        if (target && otherHost(target) !== null) problem(file, `<meta http-equiv="refresh"> goes to ${target}`);
      }
    }
  }
}

function checkJs(file, js) {
  for (const url of new Set(js.match(/https?:\/\/[^\s"'`)<>\\$,;{}]+/g) ?? [])) {
    if (otherHost(url) === null || ALLOWED_JS_URLS.has(url)) continue;
    problem(file, `the script holds the URL ${url}, which is not in the allowed list of scripts/check-dist.mjs`);
  }
}

if (!existsSync(DIST) || !statSync(DIST).isDirectory()) {
  console.error('check-dist: there is no dist/; run astro build first');
  process.exit(1);
}
if (!existsSync(path.join(DIST, 'index.html'))) problems.push('dist/ has no index.html');
if (!existsSync(path.join(DIST, 'pagefind')) || !statSync(path.join(DIST, 'pagefind')).isDirectory()) {
  problems.push('dist/ has no pagefind/ directory: the search was not built');
}

const counts = { html: 0, css: 0, js: 0 };
const seenJsUrls = new Set();
for (const file of walk(DIST)) {
  const ext = path.extname(file).toLowerCase();
  if (ext === '.html') {
    counts.html++;
    checkHtml(file, readFileSync(file, 'utf8'));
  } else if (ext === '.css') {
    counts.css++;
    for (const u of cssUrls(readFileSync(file, 'utf8'))) problem(file, `the stylesheet fetches ${u}`);
  } else if (ext === '.js' || ext === '.mjs') {
    counts.js++;
    const js = readFileSync(file, 'utf8');
    checkJs(file, js);
    for (const url of js.match(/https?:\/\/[^\s"'`)<>\\$,;{}]+/g) ?? []) seenJsUrls.add(url);
  }
}

// An allowed URL that no longer appears is a stale entry: drop it, so the list stays honest.
for (const url of ALLOWED_JS_URLS.keys()) {
  if (!seenJsUrls.has(url)) console.warn(`check-dist: note: ${url} is in the allowed list but no script holds it any more`);
}

if (problems.length > 0) {
  for (const line of problems) console.error(line);
  console.error(`check-dist: ${problems.length} problem${problems.length === 1 ? '' : 's'}: the site would ask another host for something`);
  process.exit(1);
}

const total = [...externalLinks.values()].reduce((a, b) => a + b, 0);
const hosts = [...externalLinks].sort((a, b) => b[1] - a[1]).map(([host, n]) => `${host} ${n}`).join(', ');
console.log(
  `check-dist: ${counts.html + counts.css + counts.js} files read (${counts.html} html, ${counts.css} css, ${counts.js} js), ` +
    `nothing requested from another host; ${total} external <a> links (${hosts || 'none'})`,
);
