// Generates site/src/content/docs/ from the repository's Markdown.
//
// The Markdown in the repository is the source of truth: README.md, docs/,
// samples/README.md and CHANGELOG.md read well on GitHub as they are, and this
// script turns them into the pages of the site at build time, so no copy of a
// page is ever committed and nothing under site/ is edited to change a page.
// It writes only inside site/: the pages, the three fonts and their licences.
//
// It is strict on purpose. A link in a source that would not work on the site, a
// README section it has not been told about, a page missing from the sidebar:
// each is an error that names the file and line, and the build stops, so a
// mistake in the documentation never reaches the published site in silence.
//
// The link scan follows .github/scripts/check-links.py (outside fenced code and
// code spans; inline links and images, reference definitions and HTML href/src),
// and slug() is a port of its own, so an anchor that works on GitHub works here.
// Like that script, the scan reads one line at a time, so a code span that
// wraps onto a second line is not blanked. A link-like text in one usually
// fails loudly, as a link that does not exist; the quiet case would be a
// wrapped span holding `](path)` to a file that exists, whose path would be
// rewritten inside the code. No source does that; keep such a span on one line.

import {
  copyFileSync,
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  rmSync,
  statSync,
  writeFileSync,
} from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { base, branch, repository } from '../site.config.mjs';
import { sidebarSlugs } from '../sidebar.mjs';

const SITE_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const ROOT = path.resolve(SITE_DIR, '..');
const OUT = path.join(SITE_DIR, 'src', 'content', 'docs');
const FONTS_IN = path.join(ROOT, 'src', 'Umbral', 'Assets', 'fonts');
const FONTS_OUT = path.join(SITE_DIR, 'src', 'fonts');
const LICENSES_OUT = path.join(SITE_DIR, 'public', 'licenses', 'fonts');

// The README's `## ` sections are a closed list, so that a section added to the
// README is a decision (publish it, or say it stays on GitHub) and is never
// dropped in silence.
const PUBLISHED = {
  Requirements: 'index',
  'Quick start: a first login on your own machine': 'quick-start',
  'Integrate an app': 'integrate',
};
// Sections that are not pages of their own. A link into one goes to the page
// that carries its content, or stays on GitHub when it has none.
const UNPUBLISHED = {
  Contents: { github: true },
  'Run it for real, behind HTTPS': { route: 'run/https' },
  'Your data: back it up': { route: 'run/backup' },
  Contributing: { github: true },
  License: { github: true },
};

// Documents that are not pages of the site.
const EXCLUDED_DOCS = new Set(['docs/audit-2026-10.md', 'docs/integrate.md']);
const EXCLUDED_DIRS = ['docs/history/'];

const errors = [];
const fail = (where, message) => errors.push(`${where}: ${message}`);

// ---------------------------------------------------------------------------
// Reading Markdown: the same rules as check-links.py.

const FENCE = /^ {0,3}(`{3,}|~{3,})(.*)$/;
const CODE_SPAN = /(?<!`)(`+)(?!`)(.+?)(?<!`)\1(?!`)/g;
const INLINE_LINK = /\]\(\s*(?:<([^>]*)>|((?:[^\s()]|\([^\s()]*\))*))/dg;
const REF_DEF = /^[ >]{0,3}\[[^\]]+\]:\s*(?:<([^>]*)>|(\S+))/dg;
const HTML_ATTR = /\b(?:href|src)\s*=\s*(?:"([^"]*)"|'([^']*)')/dg;
const ATX = /^ {0,3}(#{1,6})[ \t]+(.*?)(?:[ \t]+#+)?[ \t]*$/;
const SETEXT = /^ {0,3}(?:=+|-+)[ \t]*$/;
const SCHEME = /^[a-zA-Z][a-zA-Z0-9+.-]*:/;

/** The lines of a file: number, raw text, whether it is fenced code, and the text with code spans blanked. */
function scanLines(text) {
  const lines = text.split(/\r?\n/);
  if (lines.at(-1) === '') lines.pop();
  const out = [];
  let fence = null;
  lines.forEach((raw, i) => {
    const m = FENCE.exec(raw);
    let code = true;
    if (fence) {
      if (m && m[1][0] === fence[0] && m[1].length >= fence.length && !m[2].trim()) fence = null;
    } else if (m && !(m[1][0] === '`' && m[2].includes('`'))) {
      fence = m[1];
    } else {
      code = false;
    }
    out.push({
      n: i + 1,
      raw,
      code,
      blanked: code ? raw : raw.replace(CODE_SPAN, (s) => ' '.repeat(s.length)),
    });
  });
  return out;
}

/** GitHub's anchor for a heading: rendered text, lowercased, punctuation dropped, spaces to hyphens. */
function slug(heading) {
  const parts = [];
  heading.split(CODE_SPAN).forEach((piece, i) => {
    if (i % 3 === 2) {
      parts.push(piece); // the content of a code span: kept as written
      return;
    }
    if (i % 3 === 1) return; // the backtick run
    piece = piece.replace(/!?\[([^\]]*)\]\([^)]*\)|\[([^\]]*)\]\[[^\]]*\]/g, (_, a, b) => a || b || '');
    piece = piece.replace(/<[^>]+>|\*/g, '');
    parts.push(piece.replace(/(?<![\p{L}\p{N}_])_+(?=\S)|(?<=\S)_+(?![\p{L}\p{N}_])/gu, ''));
  });
  const text = parts.join('').trim().toLowerCase();
  return [...text]
    .filter((c) => /[\p{L}\p{N}\p{M}]/u.test(c) || c === ' ' || c === '-' || c === '_')
    .join('')
    .replaceAll(' ', '-');
}

/** A heading's id given the ids already used, with GitHub's -1, -2 rule for repeats. */
function uniqueId(heading, seen) {
  const id = slug(heading);
  const count = seen.get(id) ?? 0;
  seen.set(id, count + 1);
  return count === 0 ? id : `${id}-${count}`;
}

/** A title with its inline Markdown removed: code spans, emphasis and links keep their text only. */
function plainText(text) {
  return text
    .replace(/!?\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/`([^`]*)`/g, '$1')
    .replace(/[*]/g, '')
    .trim();
}

const sources = new Map();

/** A source file: its lines and its headings, with the ids GitHub gives them. */
function load(rel) {
  if (sources.has(rel)) return sources.get(rel);
  const abs = path.join(ROOT, rel);
  const lines = scanLines(readFileSync(abs, 'utf8'));
  const headings = [];
  const setext = [];
  const seen = new Map();
  let prev = null;
  for (const line of lines) {
    if (line.code) {
      prev = null;
      continue;
    }
    const m = ATX.exec(line.raw);
    if (m) {
      headings.push({ n: line.n, level: m[1].length, text: m[2], id: uniqueId(m[2], seen) });
    } else if (
      prev &&
      SETEXT.test(line.raw) &&
      !/^\s*([#>|*+-]|\d+[.)])/.test(prev.raw)
    ) {
      setext.push(line.n); // only a problem in a source that becomes a page
    }
    prev = line.raw.trim() ? line : null;
  }
  const src = { rel, abs, lines, headings, setext };
  sources.set(rel, src);
  return src;
}

/** The first line must be `# Title`; docs/ carries no frontmatter. */
function titleOf(src) {
  const first = src.lines[0]?.raw ?? '';
  if (first.startsWith('---')) {
    fail(`${src.rel}:1`, 'starts with frontmatter; the sources carry none, the site writes it');
    return '';
  }
  const m = /^# +(.*?)\s*$/.exec(first);
  if (!m) {
    fail(`${src.rel}:1`, `does not start with a "# Title" line`);
    return '';
  }
  return plainText(m[1]);
}

// ---------------------------------------------------------------------------
// The pages. A page is a list of segments; each segment is lines of one source.

const pages = [];

function addPage({ route, title, edit, segments, tabTitle }) {
  pages.push({ route, slug: route || 'index', title, edit, segments, tabTitle });
}

function walk(dir, found = []) {
  for (const entry of readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full, found);
    else if (entry.name.endsWith('.md')) found.push(path.relative(ROOT, full).split(path.sep).join('/'));
  }
  return found;
}

/** The lines of a segment: [source, from line, to line (inclusive), headings go up this many levels]. */
function segment(src, from, to, shift = 0) {
  return { src, lines: src.lines.filter((l) => l.n >= from && l.n <= to), shift };
}

// A walkthrough opens, on GitHub, with a paragraph that is only navigation:
// "Back to the [README](…). Other providers: [Discord](…) · [GitHub](…)."
// It is a GitHub reader's way back and sideways, so it stays in the source. On
// the site the sidebar does both, and "the README" is not where the reader
// came from, so the page leaves it out.
const NAVIGATION = /^Back to the \[README\]\([^)]*\)\. Other (?:providers|stores):/;

/** The lines of that paragraph, before the first `## ` heading, or null. */
function navigationParagraph(src) {
  const firstH2 = src.headings.find((h) => h.level === 2)?.n ?? src.lines.length + 1;
  const start = src.lines.find((l) => l.n < firstH2 && !l.code && NAVIGATION.test(l.raw));
  if (!start) return null;
  let to = start.n;
  while (to < src.lines.length && src.lines[to].raw.trim() && src.lines[to].n < firstH2) to++;
  const text = src.lines.slice(start.n - 1, to).map((l) => l.raw).join(' ');
  // Only links and what separates them may follow: a sentence of content in
  // the same paragraph would be dropped with it, so that is an error instead.
  const rest = text.replace(NAVIGATION, '').replace(/\[[^\]]*\]\([^)]*\)/g, '').replace(/[·.\s]/g, '');
  if (rest) {
    fail(`${src.rel}:${start.n}`, `the "Back to the README" paragraph holds more than links ("${rest.slice(0, 40)}"); give that text a paragraph of its own`);
    return null;
  }
  return { from: start.n, to };
}

function buildPages() {
  // README.md, cut into its preamble and its `## ` sections.
  const readme = load('README.md');
  if (!/^# Umbral\s*$/.test(readme.lines[0]?.raw ?? '')) fail('README.md:1', 'does not start with "# Umbral"');
  const level2 = readme.headings.filter((h) => h.level === 2);
  const sections = new Map();
  level2.forEach((h, i) => {
    const end = i + 1 < level2.length ? level2[i + 1].n - 1 : readme.lines.length;
    if (sections.has(h.text)) fail(`README.md:${h.n}`, `a second "## ${h.text}" section`);
    sections.set(h.text, { heading: h, from: h.n, to: end });
  });
  for (const [name, s] of sections) {
    if (!(name in PUBLISHED) && !(name in UNPUBLISHED)) {
      fail(
        `README.md:${s.heading.n}`,
        `the section "## ${name}" is not known to scripts/collect.mjs: add it to PUBLISHED (a page) or UNPUBLISHED (it stays on GitHub)`,
      );
    }
  }
  for (const name of Object.keys(PUBLISHED)) {
    if (!sections.has(name)) fail('README.md', `the published section "## ${name}" is missing`);
  }
  readme.sections = sections;
  readme.preambleEnd = (level2[0]?.n ?? readme.lines.length + 1) - 1;

  const section = (name) => sections.get(name);
  if (sections.has('Requirements')) {
    const req = section('Requirements');
    addPage({
      route: '',
      title: 'Umbral',
      // Starlight writes "<page> | <site>" in the browser tab, which here would be "Umbral | Umbral".
      tabTitle: 'Umbral documentation',
      edit: 'README.md',
      // The text before the first `## ` heading, without the `# Umbral` line, then the whole Requirements section.
      segments: [segment(readme, 2, readme.preambleEnd), segment(readme, req.from, req.to)],
    });
  }
  const quick = section('Quick start: a first login on your own machine');
  if (quick) {
    addPage({
      route: 'quick-start',
      title: plainText(quick.heading.text),
      edit: 'README.md',
      segments: [segment(readme, quick.from + 1, quick.to, 1)],
    });
  }

  const integrateDoc = load('docs/integrate.md');
  titleOf(integrateDoc);
  const firstH2 = integrateDoc.headings.find((h) => h.level === 2);
  const integrate = section('Integrate an app');
  if (!firstH2) fail('docs/integrate.md', 'has no "## " heading to start the page from');
  if (integrate && firstH2) {
    addPage({
      route: 'integrate',
      title: 'Integrate an app',
      edit: 'docs/integrate.md',
      segments: [
        segment(readme, integrate.from + 1, integrate.to, 1),
        segment(integrateDoc, firstH2.n, integrateDoc.lines.length),
      ],
    });
  }

  // Pages that are a whole file with their own `# ` title.
  const whole = (rel, route, fixedTitle) => {
    const src = load(rel);
    const title = titleOf(src);
    const last = src.lines.length;
    const nav = navigationParagraph(src);
    addPage({
      route,
      title: fixedTitle ?? title,
      edit: rel,
      segments: nav ? [segment(src, 2, nav.from - 1), segment(src, nav.to + 1, last)] : [segment(src, 2, last)],
    });
  };
  whole('samples/README.md', 'samples');
  whole('CHANGELOG.md', 'release-notes', 'Release notes');
  for (const rel of walk(path.join(ROOT, 'docs'))) {
    if (EXCLUDED_DOCS.has(rel) || EXCLUDED_DIRS.some((d) => rel.startsWith(d))) continue;
    whole(rel, rel.slice('docs/'.length, -'.md'.length));
  }
}

buildPages();

// Which page holds each source file, and the id each heading has on its page.
const pageOfSource = new Map();
const headingIds = new Map(); // `${rel}:${line}` -> id on the page
for (const page of pages) {
  const seen = new Map();
  for (const seg of page.segments) {
    if (!pageOfSource.has(seg.src.rel)) {
      for (const n of seg.src.setext) {
        fail(`${seg.src.rel}:${n}`, 'a Markdown heading underlined with = or - is not supported here; write it with #');
      }
    }
    pageOfSource.set(seg.src.rel, page);
    for (const line of seg.lines) {
      if (line.code) continue;
      const m = ATX.exec(line.raw);
      if (!m) continue;
      const level = m[1].length - seg.shift;
      if (level === 1) fail(`${seg.src.rel}:${line.n}`, 'a "# " heading inside a page: a page has one title, which the site writes');
      if (level < 1) fail(`${seg.src.rel}:${line.n}`, 'a heading that cannot go up a level');
      headingIds.set(`${seg.src.rel}:${line.n}`, uniqueId(m[2], seen));
    }
  }
}

// ---------------------------------------------------------------------------
// Links.

const stats = { toPages: 0, toRepository: 0 };
const pageHref = (page) => (page.route ? `${base}/${page.route}/` : `${base}/`);
const encodePath = (rel) => rel.split('/').map(encodeURIComponent).join('/');

/** The link to a file or directory of the repository on GitHub. */
function repositoryHref(rel, isDir, anchor) {
  stats.toRepository++;
  const kind = isDir ? 'tree' : 'blob';
  const where = rel ? `${repository}/${kind}/${branch}/${encodePath(rel)}` : `${repository}/${kind}/${branch}`;
  return anchor ? `${where}#${anchor}` : where;
}

/** The link to a heading on a page: `#id` on the page itself, or the page's address and `#id`. */
function pageLink(page, here, key) {
  stats.toPages++;
  const id = key ? headingIds.get(key) : undefined;
  if (page === here) return id ? `#${id}` : pageHref(page);
  return id ? `${pageHref(page)}#${id}` : pageHref(page);
}

/** The new target of a link, null to leave it alone, or an Error. */
function resolve(src, here, target) {
  if (!target || SCHEME.test(target)) return null;
  const hash = target.indexOf('#');
  const rawPath = hash < 0 ? target : target.slice(0, hash);
  const anchorRaw = hash < 0 ? '' : target.slice(hash + 1);
  let filePart = rawPath;
  let anchor = anchorRaw;
  try {
    filePart = decodeURIComponent(rawPath);
    anchor = decodeURIComponent(anchorRaw);
  } catch {
    /* keep it as written */
  }
  anchor = anchor.toLowerCase();

  const dest = !filePart
    ? src.abs
    : path.normalize(filePart.startsWith('/') ? path.join(ROOT, filePart) : path.join(path.dirname(src.abs), filePart));
  const rel = path.relative(ROOT, dest).split(path.sep).join('/');
  if (rel.startsWith('..')) return new Error(`outside the repository`);
  if (!existsSync(dest)) return new Error(`no such file ${rel}`);
  const isDir = statSync(dest).isDirectory();
  const isMarkdown = !isDir && dest.endsWith('.md');
  // A Markdown file's headings are read only when the link has an anchor to check.
  const target_ = isMarkdown && (anchor || rel === 'README.md') ? load(rel) : null;
  const heading = anchor && target_ ? target_.headings.find((h) => h.id === anchor) : null;
  if (anchor && target_ && !heading) return new Error(`no heading for #${anchorRaw} in ${rel}`);

  // README.md: its sections are on different pages.
  if (rel === 'README.md') {
    if (!anchor) return pageHref(pages.find((p) => p.route === ''));
    const owner = [...target_.sections.values()].filter((s) => s.from <= heading.n && heading.n <= s.to).at(0);
    if (!owner) return pageHref(pages.find((p) => p.route === '')); // the title
    const name = owner.heading.text;
    if (name in PUBLISHED) {
      return pageLink(pages.find((p) => p.slug === PUBLISHED[name]), here, `README.md:${heading.n}`);
    }
    const rule = UNPUBLISHED[name];
    if (rule?.route) {
      if (heading.n !== owner.heading.n) {
        return new Error(`#${anchorRaw} is inside "## ${name}", which is not a page of the site, and has no place on /${rule.route}/`);
      }
      return pageLink(pages.find((p) => p.route === rule.route), here, null);
    }
    return repositoryHref('README.md', false, anchorRaw);
  }

  // A page's source (docs/integrate.md is part of /integrate/).
  const page = pageOfSource.get(rel);
  if (page) return pageLink(page, here, heading ? `${rel}:${heading.n}` : null);

  // Anything else that exists: GitHub shows it.
  return repositoryHref(rel, isDir, anchorRaw);
}

/** The place of each link target in a line, from the same patterns as check-links.py. */
function linkTargets(line) {
  const found = [];
  for (const pattern of [INLINE_LINK, REF_DEF, HTML_ATTR]) {
    for (const m of line.blanked.matchAll(pattern)) {
      // The first group that took part holds the target (the `<...>` form or the plain one).
      const group = m.indices.findIndex((range, i) => i > 0 && range && m[i]);
      if (group > 0) found.push({ start: m.indices[group][0], end: m.indices[group][1], target: m[group] });
    }
  }
  return found.sort((a, b) => b.start - a.start);
}

function renderBody(page) {
  const out = [];
  for (const seg of page.segments) {
    const body = [];
    for (const line of seg.lines) {
      let text = line.raw;
      if (!line.code) {
        for (const { start, end, target } of linkTargets(line)) {
          const next = resolve(seg.src, page, target);
          if (next instanceof Error) {
            fail(`${seg.src.rel}:${line.n}`, `${target}: ${next.message}`);
          } else if (next !== null) {
            text = text.slice(0, start) + next + text.slice(end);
          }
        }
        if (seg.shift) text = text.replace(/^( {0,3})#{1,6}(?=[ \t])/, (h) => h.slice(0, h.length - seg.shift));
      }
      body.push(text);
    }
    out.push(body.join('\n').replace(/^\n+|\n+$/g, ''));
  }
  return out.join('\n\n');
}

// ---------------------------------------------------------------------------
// The sidebar names every page, and only pages.

const generated = new Set(pages.map((p) => p.slug));
const named = new Set(sidebarSlugs());
for (const slugName of generated) {
  if (!named.has(slugName)) fail('site/sidebar.mjs', `the page "${slugName}" is not in the sidebar`);
}
for (const slugName of named) {
  if (!generated.has(slugName)) fail('site/sidebar.mjs', `names the page "${slugName}", which was not generated`);
}

// ---------------------------------------------------------------------------
// Write.

const rendered = pages.map((page) => ({ page, body: renderBody(page) }));

if (errors.length > 0) {
  for (const line of errors) console.error(line);
  console.error(`collect: ${errors.length} problem${errors.length === 1 ? '' : 's'}, nothing written`);
  process.exit(1);
}

rmSync(OUT, { recursive: true, force: true });
for (const { page, body } of rendered) {
  const file = path.join(OUT, `${page.slug}.md`);
  mkdirSync(path.dirname(file), { recursive: true });
  const front = [
    '---',
    `title: ${JSON.stringify(page.title)}`,
    `editUrl: ${JSON.stringify(`${repository}/edit/${branch}/${page.edit}`)}`,
    ...(page.tabTitle ? ['head:', '  - tag: title', `    content: ${JSON.stringify(page.tabTitle)}`] : []),
    '---',
    '',
  ];
  writeFileSync(file, `${front.join('\n')}\n${body}\n`);
}

// The three fonts the stylesheet names, and their licences, which travel with them.
rmSync(FONTS_OUT, { recursive: true, force: true });
rmSync(path.join(SITE_DIR, 'public', 'licenses'), { recursive: true, force: true });
mkdirSync(FONTS_OUT, { recursive: true });
mkdirSync(LICENSES_OUT, { recursive: true });
let fonts = 0;
for (const name of readdirSync(FONTS_IN)) {
  if (name.endsWith('.woff2')) {
    copyFileSync(path.join(FONTS_IN, name), path.join(FONTS_OUT, name));
    fonts++;
  } else if (/^OFL-.*\.txt$/.test(name)) {
    copyFileSync(path.join(FONTS_IN, name), path.join(LICENSES_OUT, name));
  }
}

console.log(
  `collect: ${pages.length} pages written to src/content/docs/, ` +
    `${stats.toPages} links to pages and ${stats.toRepository} to the repository on GitHub, ${fonts} fonts copied`,
);
