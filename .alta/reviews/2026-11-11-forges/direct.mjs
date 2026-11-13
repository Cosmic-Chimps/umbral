// Direct OpenID Connect walk against a forge: prints the id_token payload and userinfo.
// node direct.mjs <issuer> <client_id> <client_secret> <username> <password> <label>
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import http from 'node:http';
import crypto from 'node:crypto';
const [issuer, clientId, clientSecret, username, password, label] = process.argv.slice(2);
const disc = await (await fetch(issuer + '/.well-known/openid-configuration')).json();
const redirect = 'http://127.0.0.1:3999/cb';
const verifier = crypto.randomBytes(32).toString('base64url');
const challenge = crypto.createHash('sha256').update(verifier).digest('base64url');
const state = crypto.randomBytes(8).toString('hex');
const nonce = crypto.randomBytes(8).toString('hex');
let resolveCode; const codeP = new Promise(r => resolveCode = r);
const server = http.createServer((req, res) => { const u = new URL(req.url, redirect); res.end('ok'); resolveCode(Object.fromEntries(u.searchParams)); });
server.listen(3999, '127.0.0.1');
const browser = await chromium.launch({ channel: 'chrome' });
const page = await browser.newPage();
const url = disc.authorization_endpoint + '?' + new URLSearchParams({ client_id: clientId, redirect_uri: redirect, response_type: 'code', scope: 'openid profile email', state, nonce, code_challenge: challenge, code_challenge_method: 'S256' });
await page.goto(url);
if (page.url().includes('/users/sign_in')) { await page.fill('#user_login', username); await page.fill('#user_password', password); await page.click('[data-testid="sign-in-button"], button[type=submit]'); await page.waitForLoadState('load'); }
if (page.url().includes('/user/login')) {
  await page.fill('#user_name', username);
  await page.fill('#password', password);
  await page.click('form[action="/user/login"] button.primary');
  await page.waitForLoadState('load');
}
console.log(label, 'after login:', page.url());
if (!page.url().startsWith(redirect)) {
  const grant = page.locator('form[action*="grant"] button:not([name=button])').first();
  const title = await page.title();
  const text = await page.locator('form[action*="grant"]').first().innerText().catch(() => '(no grant form; page title: ' + title + ')');
  console.log(label, 'grant page text:', text.replace(/\s+/g, ' ').slice(0, 400));
  const btn = page.locator('button:has-text("Authorize"), input[type=submit][value="Authorize"]').first();
  if (await btn.count()) await btn.evaluate(b => b.click()); else { console.log(label, 'no consent button; page text:', (await page.locator('body').innerText()).replace(/\s+/g, ' ').slice(0, 500)); await page.screenshot({ path: '/tmp/f0rg/out/' + label + '-debug.png', fullPage: true }); }
}
const params = await Promise.race([codeP, new Promise((_, rej) => setTimeout(() => rej(new Error('no redirect in 20s; url ' + page.url())), 20000))]).catch(e => ({ error: String(e) }));
console.log(label, 'redirect params:', JSON.stringify(params));
if (params.code) {
  const tok = await (await fetch(disc.token_endpoint, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded', Authorization: 'Basic ' + Buffer.from(clientId + ':' + clientSecret).toString('base64') }, body: new URLSearchParams({ grant_type: 'authorization_code', code: params.code, redirect_uri: redirect, code_verifier: verifier }) })).json();
  console.log(label, 'token response keys:', Object.keys(tok).join(','), tok.error ? JSON.stringify(tok) : '');
  if (tok.id_token) {
    const [h, p] = tok.id_token.split('.');
    console.log(label, 'id_token header:', Buffer.from(h, 'base64url').toString());
    console.log(label, 'id_token payload:', Buffer.from(p, 'base64url').toString());
  }
  const info = await (await fetch(disc.userinfo_endpoint, { headers: { Authorization: 'Bearer ' + tok.access_token } })).text();
  console.log(label, 'userinfo:', info);
}
await browser.close(); server.close();
