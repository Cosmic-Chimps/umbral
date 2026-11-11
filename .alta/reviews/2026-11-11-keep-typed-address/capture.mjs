// Capture walk for task k3ep (keep the typed address after a refused add).
// Args: base URL, setup token, output folder. Every refused state is a real
// submission of the add form in the browser: the cookie is single-use, so each
// capture refuses again.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, setupToken, out] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const credential = 'a-very-good-credential-k3ep';

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

async function token() {
  await page.goto(base + '/admin/apps');
  return page.locator('input[name=__RequestVerificationToken]').first().inputValue();
}

async function post(path, form) {
  const t = await token();
  const r = await page.request.post(base + path, {
    form: { __RequestVerificationToken: t, ...form },
    headers: { Origin: base },
    maxRedirects: 0,
  });
  return r.headers()['location'] ?? `status ${r.status()}`;
}

console.log('passkey connector:', await post('/admin/connectors/passkey', { enrolment: 'open', shared: 'on' }));
const loc = await post('/admin/apps', {
  name: 'Wiki', redirectUri: 'https://wiki.example.com/oauth/callback', postLogoutRedirectUri: '',
  appType: 'web', connectorChoice: 'all-shared',
});
const app = decodeURIComponent(loc.split('?')[0].split('/').pop());
console.log(await post(`/admin/apps/${app}/post-logout-uris`, { uri: 'https://wiki.example.com/signed-out' }));

// A long address with one wrong character: a wildcard in the host.
const typedRedirect =
  'https://staging.*.wiki.example.com/a/rather/long/path/that/must/wrap/on/a/narrow/screen/oauth/callback?tenant=engineering';
// A post-logout address with a fragment.
const typedPostLogout = 'https://wiki.example.com/signed-out#bye';

// Type into the add field of a list and press Add, as an operator does.
async function refuse(marker, typed) {
  await page.goto(base + `/admin/apps/${app}`);
  await page.waitForLoadState('networkidle');
  const form = page.locator(`[data-${marker}] form:has(input[name=uri][type=text])`);
  await form.locator('input[type=text]').fill(typed);
  await form.locator('button[type=submit]').click();
  await page.waitForLoadState('networkidle');
}

const states = [
  ['refused-invalid', () => refuse('redirect-uris', typedRedirect)],
  ['refused-invalid-post-logout', () => refuse('post-logout-uris', typedPostLogout)],
];

const metrics = [];
for (const theme of ['dark', 'light']) {
  await context.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
  for (const width of [1280, 360]) {
    await page.setViewportSize({ width, height: 900 });
    for (const [name, act] of states) {
      await act();
      const url = page.url();
      const values = await page.evaluate(() =>
        [...document.querySelectorAll('input[id^="redirect-uri-add-"], input[id^="post-logout-uri-add-"]')].map(
          (i) => `${i.id.split('-add-')[0]}=${JSON.stringify(i.value)} invalid=${i.getAttribute('aria-invalid')}`,
        ),
      );
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      metrics.push(`${name}-${width}-${theme}: url=${url.replace(base, '')} overflow=${overflow} ${values.join(' ')}`);
      await page.screenshot({ path: `${out}/${name}-${width}-${theme}.png`, fullPage: true });
    }
  }
}

// A refresh after a refusal: the cookie was read, the field is empty.
await context.addCookies([{ name: 'umbral_theme', value: 'dark', url: base }]);
await page.setViewportSize({ width: 1280, height: 900 });
await refuse('redirect-uris', typedRedirect);
await page.reload();
await page.waitForLoadState('networkidle');
const after = await page.locator('input[id^="redirect-uri-add-"]').inputValue();
metrics.push(`refresh-1280-dark: field=${JSON.stringify(after)}`);
await page.screenshot({ path: `${out}/refused-invalid-refresh-1280-dark.png`, fullPage: true });

console.log(metrics.join('\n'));
await browser.close();
