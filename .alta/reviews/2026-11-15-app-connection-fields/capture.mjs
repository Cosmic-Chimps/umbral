// Capture walk for task 3694 (Client ID and Issuer URL one per row).
// Args: base URL, setup token, output folder. Captures an app's pane at 1280
// and 360, in both themes (the created state, with the secret, is the docs
// image docs/images/app-created-*.png), and measures whether each value shows whole and how many
// characters its field holds.
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
import fs from 'node:fs';

const [base, setupToken, out] = process.argv.slice(2);
fs.mkdirSync(out, { recursive: true });
const credential = 'a-very-good-credential-3694';

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

async function post(path, form) {
  await page.goto(base + '/admin/apps');
  const t = await page.locator('input[name=__RequestVerificationToken]').first().inputValue();
  const r = await page.request.post(base + path, {
    form: { __RequestVerificationToken: t, ...form },
    headers: { Origin: base },
    maxRedirects: 0,
  });
  return r.headers()['location'] ?? `status ${r.status()}`;
}

console.log('passkey connector:', await post('/admin/connectors/passkey', { enrolment: 'open', shared: 'on' }));
const created = await post('/admin/apps', {
  name: 'Wiki', redirectUri: 'https://wiki.example.com/oauth/callback', postLogoutRedirectUri: '',
  appType: 'web', connectorChoice: 'all-shared',
});
const appPath = created.split('?')[0];

// What each field holds: whether its value shows whole, and how many
// characters of the mono font fit in it. Never the secret.
const measure = () =>
  page.evaluate(() =>
    ['client-id-', 'issuer-'].map((prefix) => {
      const el = document.querySelector(`input[id^="${prefix}"]`);
      const value = el.value;
      const whole = el.scrollWidth <= el.clientWidth;
      // Fill the field until it scrolls, then put its value back.
      let holds = 0;
      while (holds < 200) {
        el.value = 'x'.repeat(holds + 1);
        if (el.scrollWidth > el.clientWidth) break;
        holds++;
      }
      el.value = value;
      return `${prefix}${JSON.stringify(value)} whole=${whole} holds=${holds} chars`;
    }),
  );

const metrics = [];
for (const theme of ['dark', 'light']) {
  await context.addCookies([{ name: 'umbral_theme', value: theme, url: base }]);
  for (const width of [1280, 360]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(base + appPath);
    await page.waitForLoadState('networkidle');
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    await page.screenshot({ path: `${out}/pane-${width}-${theme}.png`, fullPage: true });
    metrics.push(`pane-${width}-${theme}: overflow=${overflow} ${(await measure()).join(' ')}`);
  }
}

console.log(metrics.join('\n'));
await browser.close();
