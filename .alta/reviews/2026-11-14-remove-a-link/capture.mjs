// Capture walk for the design review of spec 018 (Remove link on the Users screen).
// Usage: node capture.mjs <base> <admin-credential> <out> <setup-token> <database-file>
//   One fresh Release instance with the development exception (http://127.0.0.1:PORT), started
//   with HTTPS_PROXY=http://127.0.0.1:9. First-run setup goes through the real form with the setup
//   token from its log. Users, links and passkeys can only come from real sign-ins, so they are
//   written into the instance's SQLite file (sqlite3 CLI) before each state; everything on the
//   screen (the list, the 422 confirm block, the removal, the notices) goes through the browser.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';

const [base, credential, out, setupToken, dbFile] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const themes = ['dark', 'light'];
const widths = [1280, 360];
const metrics = [];

const sql = (text) => execFileSync('sqlite3', [dbFile], { input: text }).toString();
const fp = (kind, subject) => crypto.createHash('sha256').update(`${kind}\n${subject}`, 'utf8').digest('base64url');
const forgeIssuer = 'http://127.0.0.1:9/forge';
const q = (s) => (s === null ? 'NULL' : `'${String(s).replace(/'/g, "''")}'`);

// The people on the screen. Dates are fixed so the lists are the same on every run.
const people = [
  // A card with two links and a passkey; its forge link carries passkeys of both kinds.
  { id: 'u-alice', name: 'Alice Moreau', email: 'alice@example.org', seen: '2026-11-13T09:00:00Z',
    links: [['github', 'gh-1001', '2026-06-02T10:00:00Z', '2026-11-13T09:00:00Z'],
            ['oidc', `${forgeIssuer}#stranger-7`, '2026-11-02T18:20:00Z', '2026-11-12T21:05:00Z']],
    passkeys: [['pk-alice-mac', "Alice's MacBook", '2026-07-15T08:00:00Z', null],
               ['pk-str-phone', 'Pixel 9', '2026-11-03T19:00:00Z', 'link:FORGE'],
               ['pk-str-key', 'YubiKey', '2026-11-04T20:00:00Z', 'passkey:pk-str-phone'],
               ['pk-tablet', 'iPad', '2026-11-05T07:30:00Z', null]] },
  // One link only: no list.
  { id: 'u-bob', name: 'Bob Lindqvist', email: 'bob@example.org', seen: '2026-11-12T09:00:00Z',
    links: [['discord', 'dc-2002', '2026-08-20T12:00:00Z', '2026-11-12T09:00:00Z']], passkeys: [] },
  // Two links of the same provider: same label, different dates.
  { id: 'u-carol', name: 'Carol Nakamura', email: 'carol@example.org', seen: '2026-11-11T09:00:00Z',
    links: [['github', 'gh-3003', '2026-05-10T09:00:00Z', '2026-11-11T09:00:00Z'],
            ['github', 'gh-3999', '2026-10-28T22:10:00Z', '2026-11-01T23:40:00Z']], passkeys: [] },
  // Two links, no passkeys: the plain block, then the removal.
  { id: 'u-dave', name: 'Dave Okafor', email: 'dave@example.org', seen: '2026-11-10T09:00:00Z',
    links: [['github', 'gh-4004', '2026-04-01T09:00:00Z', '2026-11-10T09:00:00Z'],
            ['discord', 'dc-4999', '2026-10-30T09:00:00Z', '2026-11-09T09:00:00Z']], passkeys: [] },
  // One link and a passkey added through it: confirming is refused as the last way in.
  { id: 'u-erin', name: 'Erin Castillo', email: 'erin@example.org', seen: '2026-11-09T09:00:00Z',
    links: [['google', 'go-5005', '2026-03-03T09:00:00Z', '2026-11-09T09:00:00Z']],
    passkeys: [['pk-erin', 'Erin phone', '2026-09-09T09:00:00Z', 'link:SELF']] },
  // Removed-too list only.
  { id: 'u-grace', name: 'Grace Ibsen', email: 'grace@example.org', seen: '2026-11-08T09:00:00Z',
    links: [['github', 'gh-6006', '2026-02-02T09:00:00Z', '2026-11-08T09:00:00Z'],
            ['oidc', `${forgeIssuer}#stranger-8`, '2026-11-01T09:00:00Z', '2026-11-07T09:00:00Z']],
    passkeys: [['pk-grace-str', 'Galaxy S25', '2026-11-02T09:00:00Z', 'link:FORGE']] },
  // Check-these list only.
  { id: 'u-henry', name: 'Henry Achebe', email: 'henry@example.org', seen: '2026-11-07T09:00:00Z',
    links: [['github', 'gh-7007', '2026-01-15T09:00:00Z', '2026-11-07T09:00:00Z'],
            ['oidc', `${forgeIssuer}#stranger-9`, '2026-10-20T09:00:00Z', '2026-11-06T09:00:00Z']],
    passkeys: [['pk-henry-old', 'ThinkPad', '2026-03-01T09:00:00Z', null],
               ['pk-henry-new', 'Unknown phone', '2026-10-22T09:00:00Z', null]] },
  // Two links, for the "already removed" notice.
  { id: 'u-frank', name: 'Frank Weber', email: 'frank@example.org', seen: '2026-11-06T09:00:00Z',
    links: [['github', 'gh-8008', '2026-02-20T09:00:00Z', '2026-11-06T09:00:00Z'],
            ['google', 'go-8999', '2026-10-25T09:00:00Z', '2026-11-05T09:00:00Z']], passkeys: [] },
];

function seed() {
  let s = 'PRAGMA foreign_keys = ON;\n';
  for (const p of people) {
    s += `DELETE FROM users WHERE id = ${q(p.id)};\n`;
    s += `INSERT INTO users (id, display_name, email, email_verified, is_enabled, first_seen_utc, last_seen_utc)
          VALUES (${q(p.id)}, ${q(p.name)}, ${q(p.email)}, 1, 1, ${q(p.links[0][2])}, ${q(p.seen)});\n`;
    for (const [kind, subject, linked, used] of p.links) {
      s += `DELETE FROM linked_accounts WHERE provider_kind = ${q(kind)} AND provider_subject = ${q(subject)};\n`;
      s += `INSERT INTO linked_accounts (provider_kind, provider_subject, user_id, asserted_email, asserted_email_verified, linked_utc, last_used_utc)
            VALUES (${q(kind)}, ${q(subject)}, ${q(p.id)}, ${q(p.email)}, 1, ${q(linked)}, ${q(used)});\n`;
    }
    const forge = p.links.find((l) => l[0] === 'oidc');
    for (const [id, nick, created, via] of p.passkeys) {
      const added =
        via === 'link:FORGE' ? `link:${fp('oidc', forge[1])}` :
        via === 'link:SELF' ? `link:${fp(p.links[0][0], p.links[0][1])}` : via;
      s += `INSERT INTO passkeys (id, user_id, credential_id, public_key, nickname, created_utc, added_via)
            VALUES (${q(id)}, ${q(p.id)}, randomblob(16), randomblob(32), ${q(nick)}, ${q(created)}, ${q(added)});\n`;
    }
  }
  sql(s);
}

const browser = await chromium.launch({ channel: 'chrome' });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
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

// The forge, so its links are labelled by name (saved disabled: the issuer is refused).
await page.goto(base + '/admin/connectors/new');
await page.selectOption('#add-kind', 'oidc');
await page.fill('#slug', 'forge');
await page.fill('#displayName', 'Open forge');
await page.fill('#issuer', forgeIssuer);
await page.fill('#clientId', 'umbral-client');
await page.fill('#clientSecret', 'not-a-real-secret-018');
await page.locator('form[data-add-connector] input[name=emailTrust][value=off]').check();
await page.locator('form[data-add-connector] button[type=submit]').click();
await page.waitForLoadState('networkidle');
console.log('forge added: ' + page.url());

async function setTheme(theme) {
  await context.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
}
async function shot(name, width, theme) {
  await page.waitForLoadState('networkidle');
  const o = await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, iw: window.innerWidth }));
  metrics.push(`${name}-${width}-${theme}: overflow=${o.sw > o.iw} (scrollWidth ${o.sw}, innerWidth ${o.iw})`);
  await page.screenshot({ path: `${out}/${name}-${width}-${theme}.png`, fullPage: true });
}
const mainText = async () => (await page.locator('main').innerText()).replace(/\s+/g, ' ');
async function each(name, prepare, report, only) {
  for (const theme of only?.themes ?? themes) {
    await setTheme(theme);
    for (const width of only?.widths ?? widths) {
      seed();
      await page.setViewportSize({ width, height: 900 });
      const status = await prepare();
      if (report) console.log(`${name} ${width} ${theme}${status ? ' [' + status + ']' : ''}: ${await report()}`);
      await shot(name, width, theme);
    }
  }
}
const users = (term) => page.goto(base + '/admin/users?search=' + encodeURIComponent(term));
const card = (id) => `article[data-user="${id}"]`;
async function openBlock(userId, kind, subject) {
  const row = `${card(userId)} li[data-user-link="${fp(kind, subject)}"]`;
  const [r] = await Promise.all([
    page.waitForResponse((x) => x.request().method() === 'POST' && x.url().includes('/links/')),
    page.locator(`${row} [data-user-link-remove]`).click(),
  ]);
  await page.waitForLoadState('networkidle');
  return `POST ${r.status()}`;
}
async function confirmBlock() {
  const [r] = await Promise.all([
    page.waitForResponse((x) => x.request().method() === 'POST' && x.url().includes('/links/')),
    page.locator('[data-user-link-confirm] button[type=submit]').click(),
  ]);
  await page.waitForLoadState('networkidle');
  return `POST ${r.status()} -> ${page.url().replace(base, '')}`;
}
const block = async () => (await page.locator('[data-user-link-confirm]').first().innerText().catch(() => '(none)')).replace(/\s+/g, ' ');
const notice = async () => (await page.locator('[data-notice]').first().innerText().catch(() => '(none)')).replace(/\s+/g, ' ');

// 1-3. The cards.
await each('card-two-links-passkey', () => users('Alice'), async () => (await page.locator(card('u-alice')).innerText()).replace(/\s+/g, ' '));
await each('card-one-link', () => users('Bob'), async () => `list drawn = ${await page.locator(`${card('u-bob')} [data-user-links]`).count()}`);
await each('card-same-provider', () => users('Carol'), async () => (await page.locator(`${card('u-carol')} [data-user-links]`).innerText()).replace(/\s+/g, ' '));
// 6. The confirm block: plain, with a removed-too list, with a check-these list, with both.
await each('block-plain', async () => { await users('Dave'); return openBlock('u-dave', 'discord', 'dc-4999'); }, block);
await each('block-gone', async () => { await users('Grace'); return openBlock('u-grace', 'oidc', `${forgeIssuer}#stranger-8`); }, block);
await each('block-check', async () => { await users('Henry'); return openBlock('u-henry', 'oidc', `${forgeIssuer}#stranger-9`); }, block);
await each('block-both', async () => { await users('Alice'); return openBlock('u-alice', 'oidc', `${forgeIssuer}#stranger-7`); }, block);
// 7. Focus in the block, 1280 dark.
await each('block-focus', async () => {
  await users('Dave');
  const s = await openBlock('u-dave', 'discord', 'dc-4999');
  await page.waitForTimeout(200);
  return s;
}, async () => `activeElement: ${await page.evaluate(() => {
  const a = document.activeElement;
  return a ? `${a.tagName} ${[...a.attributes].map((x) => x.name).filter((n) => n.startsWith('data-')).join(' ')}` : '(none)';
})}`, { themes: ['dark'], widths: [1280] });
// 4. The Success notice after a removal.
await each('notice-removed', async () => { await users('Dave'); await openBlock('u-dave', 'discord', 'dc-4999'); return confirmBlock(); }, notice);
// 5. The two Warning notices: the last way in, and a link already removed.
await each('notice-last', async () => { await users('Erin'); await openBlock('u-erin', 'google', 'go-5005'); return confirmBlock(); }, notice);
await each('notice-not-found', async () => {
  await users('Frank');
  await openBlock('u-frank', 'google', 'go-8999');
  sql(`DELETE FROM linked_accounts WHERE provider_kind = 'google' AND provider_subject = 'go-8999';`);
  return confirmBlock();
}, notice);

console.log(metrics.join('\n'));
await browser.close();
