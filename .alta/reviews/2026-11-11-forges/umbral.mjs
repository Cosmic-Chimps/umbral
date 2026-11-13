// The walk through Umbral: set up, add the two forge connectors, create a public app, sign in through each.
// node umbral.mjs <base> <setup-token> <out>
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import http from 'node:http';
import crypto from 'node:crypto';
import fs from 'node:fs';
const [base, setupToken, out] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const credential = 'a-very-good-credential-f0rg';
const forges = {
  forgejo: { issuer: 'http://localhost:3001', ...JSON.parse(fs.readFileSync('/tmp/f0rg-forgejo-app.json')) },
  gitea: { issuer: 'http://localhost:3002', ...JSON.parse(fs.readFileSync('/tmp/f0rg-gitea-app.json')) },
};
const browser = await chromium.launch({ channel: 'chrome' });
const admin = await browser.newPage();
await admin.goto(base + '/admin/setup');
await admin.fill('[name=token]', setupToken);
await admin.fill('[name=credential]', credential);
await admin.fill('[name=confirm]', credential);
await admin.press('[name=confirm]', 'Enter');
await admin.waitForLoadState('load');
if (admin.url().includes('/admin/signin')) { await admin.fill('[name=credential]', credential); await admin.press('[name=credential]', 'Enter'); await admin.waitForLoadState('load'); }
async function token() { await admin.goto(base + '/admin/apps'); return admin.locator('input[name=__RequestVerificationToken]').first().inputValue(); }
async function post(path, form) {
  const t = await token();
  const r = await admin.request.post(base + path, { form: { __RequestVerificationToken: t, ...form }, headers: { Origin: base }, maxRedirects: 0 });
  const body = await r.text();
  const problems = [...body.matchAll(/class="um-(?:error|problem|notice)[^"]*"[^>]*>([^<]{5,200})/g)].map(m => m[1].trim());
  return { status: r.status(), location: r.headers()['location'], problems };
}
for (const [slug, f] of Object.entries(forges)) {
  const r = await post('/admin/connectors', { kind: 'oidc', slug, displayName: slug[0].toUpperCase() + slug.slice(1), issuer: f.issuer, clientId: f.client_id, clientSecret: f.client_secret, shared: 'true', allowedDomains: '' });
  console.log('add connector', slug, JSON.stringify(r));
}
await admin.goto(base + '/admin/connectors');
console.log('connectors list:', (await admin.locator('main').innerText()).replace(/\s+/g, ' ').slice(0, 600));
const redirect = 'http://127.0.0.1:3999/cb';
const app = await post('/admin/apps', { name: 'Forge test', redirectUri: redirect, postLogoutRedirectUri: '', appType: 'spa', connectorChoice: 'all-shared' });
console.log('create app', JSON.stringify(app));
const clientId = decodeURIComponent(app.location.split('?')[0].split('/').pop());
let resolveCode; let codeP;
const server = http.createServer((req, res) => { const u = new URL(req.url, redirect); res.end('ok'); resolveCode(Object.fromEntries(u.searchParams)); });
server.listen(3999, '127.0.0.1');
async function signIn(label, slug, username, password, shot) {
  codeP = new Promise(r => resolveCode = r);
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await ctx.newPage();
  const verifier = crypto.randomBytes(32).toString('base64url');
  const challenge = crypto.createHash('sha256').update(verifier).digest('base64url');
  const state = crypto.randomBytes(8).toString('hex');
  await page.goto(base + '/connect/authorize?' + new URLSearchParams({ client_id: clientId, response_type: 'code', redirect_uri: redirect, scope: 'openid email profile', state, nonce: state, code_challenge: challenge, code_challenge_method: 'S256' }));
  await page.waitForLoadState('load');
  if (shot) await page.screenshot({ path: out + '/' + shot, fullPage: true });
  console.log(label, 'sign-in page buttons:', (await page.locator('button, a').allInnerTexts()).map(s => s.trim()).filter(s => s.startsWith('Sign in')).join(' | '));
  await page.locator('text="Sign in with ' + ({ forgejo: 'Forgejo', gitea: 'Gitea', gitlab: 'GitLab' })[slug] + '"').first().click();
  await page.waitForLoadState('load');
  if (page.url().includes('/users/sign_in')) { await page.fill('#user_login', username); await page.fill('#user_password', password); await page.click('[data-testid="sign-in-button"], button[type=submit]'); await page.waitForLoadState('load'); }
if (page.url().includes('/users/sign_in')) { await page.fill('#user_login', username); await page.fill('#user_password', password); await page.click('[data-testid="sign-in-button"], button[type=submit]'); await page.waitForLoadState('load'); }
  if (page.url().includes('/user/login')) { await page.fill('#user_name', username); await page.fill('#password', password); await page.click('form[action="/user/login"] button.primary'); await page.waitForLoadState('load'); }
  const btn = page.locator('button:has-text("Authorize"), input[type=submit][value="Authorize"]').first();
  if (await btn.count()) await btn.evaluate(b => b.click()); else { console.log(label, 'no consent button; page text:', (await page.locator('body').innerText()).replace(/\s+/g, ' ').slice(0, 500)); await page.screenshot({ path: '/tmp/f0rg/out/' + label + '-debug.png', fullPage: true }); }
  const params = await Promise.race([codeP, new Promise((_, rej) => setTimeout(() => rej(new Error('no redirect in 25s; url ' + page.url())), 25000))]).catch(e => ({ error: String(e), text: '' }));
  if (params.error) { params.text = (await page.locator('main').innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 300); console.log(label, 'FAILED', JSON.stringify(params)); await ctx.close(); return; }
  const tok = await (await fetch(base + '/connect/token', { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: new URLSearchParams({ grant_type: 'authorization_code', client_id: clientId, code: params.code, redirect_uri: redirect, code_verifier: verifier }) })).json();
  if (tok.id_token) console.log(label, 'Umbral id_token payload:', Buffer.from(tok.id_token.split('.')[1], 'base64url').toString()); else console.log(label, 'token error', JSON.stringify(tok));
  if (tok.access_token) console.log(label, 'Umbral userinfo:', await (await fetch(base + '/connect/userinfo', { headers: { Authorization: 'Bearer ' + tok.access_token } })).text());
  await ctx.close();
}
await signIn('FORGEJO-ANA', 'forgejo', 'ana', 'Correct-Horse-Battery-1', 'signin-1280-light.png');
await signIn('GITEA-ANA', 'gitea', 'ana', 'Correct-Horse-Battery-1');
await signIn('GITEA-MALLORY', 'gitea', 'mallory', 'Correct-Horse-Battery-1');
await signIn('FORGEJO-BOB-SUSPENDED', 'forgejo', 'bob', 'Correct-Horse-Battery-1');
await admin.goto(base + '/admin/users');
console.log('users screen:', (await admin.locator('main').innerText()).replace(/\s+/g, ' ').slice(0, 900));
await browser.close(); server.close();
