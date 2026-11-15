// Walks a fresh Umbral through the real UI and captures the screens of the
// documentation, each in the dark and the light theme, into out/raw/. run.sh
// starts the instance and claims it (reset-credential); optimise.mjs runs
// after this one. Neither the password nor the app's secret is ever read into
// a file or printed (the secret is shown on screen and captured, never read).
//
// The pictures show only what Umbral renders: no cookie and no edit of the page.
// With no `umbral_theme` cookie Umbral follows the system, so a theme is
// `page.emulateMedia({ colorScheme })`, which switches live and keeps the
// one-time state of a page.
import { chromium } from '@playwright/test';
import { mkdirSync } from 'node:fs';

const base = process.env.UMBRAL_URL ?? 'http://localhost:8080';
const password = process.env.ADMIN_PASSWORD;
if (!password) throw new Error('ADMIN_PASSWORD is not set');
const rawDir = 'out/raw';
mkdirSync(rawDir, { recursive: true });

// ---------------------------------------------------------------------------
// Framing, one entry per screen. Tune it here, without touching the walk.
//   width, height  the viewport
//   fullPage       the whole page, or
//   clip           { selectors, padding }: the union of the boxes of the
//                  elements the selectors name (each must match), plus a
//                  padding in px (a number, or { top, right, bottom, left }),
//                  clamped to the page. A padding must stay under the gap to
//                  the next element, or the clip cuts into it.
// ---------------------------------------------------------------------------
const operator = { width: 1180, height: 760 };
const person = { width: 760, height: 600 };
const card = (selector) => ({ ...person, clip: { selectors: [selector], padding: 32 } });
const FRAMES = {
  // The detail pane from its header (name and pill) through the Set-up section.
  'connector-github': {
    ...operator,
    clip: {
      selectors: [
        '[data-connector] > div:has(> #connector-heading)',
        '[data-connector] > div:has(> h3:text-is("Set-up"))',
      ],
      padding: { top: 24, right: 24, bottom: 16, left: 24 },
    },
  },
  // The detail pane from its header through the issuer URL: the notice, the
  // shown-once warning, the secret, the client ID.
  'app-created': {
    ...operator,
    clip: {
      selectors: [
        '[data-app] > div:has(> #app-heading)',
        '[data-app] [data-notice="app-created"]',
        '[data-app] [data-secret-once]',
        '[data-app] div:has(> label:text-is("Issuer URL"))',
      ],
      padding: { top: 24, right: 24, bottom: 12, left: 24 },
    },
  },
  'sign-in': { ...person, fullPage: true },
  'passkey-step': card('[data-screen="passkey-signin"]'),
  'fediverse-step': card('[data-screen="fediverse-account"]'),
  'your-passkeys': { ...person, fullPage: true },
  // The page heading through the Passkeys and Password sign-in cards.
  'operator-credential': {
    ...operator,
    clip: {
      selectors: [
        '[data-screen="credential"] > div:has(> h1)',
        '[data-section="passkeys"]',
        '[data-section="password-signin"]',
      ],
      padding: { top: 24, right: 24, bottom: 16, left: 24 },
    },
  },
  'sign-in-branded': { ...person, fullPage: true },
};
const THEMES = ['dark', 'light'];
const SCALE = 2;

// What the app asks for, and what its sign-in page is reached with.
const APP = { name: 'Larder', redirectUri: 'http://localhost:3000/callback' };
const ACCENT = '#d9480f';
const authorizeQuery = (clientId) =>
  `client_id=${encodeURIComponent(clientId)}&response_type=code` +
  `&redirect_uri=${encodeURIComponent(APP.redirectUri)}&scope=openid%20email%20profile` +
  '&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256&state=docs';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
// Headless Chromium hints glyphs to whole pixels by default, which spaces the
// letters of a word unevenly ("T heme"). With hinting off it places them as a
// real screen does.
const browser = await chromium.launch({ args: ['--font-render-hinting=none'] });
const log = (message) => console.log(message);

async function newContext(viewport, { passkeys = false } = {}) {
  const context = await browser.newContext({ viewport, deviceScaleFactor: SCALE, locale: 'en-US' });
  const page = await context.newPage();
  if (passkeys) {
    const cdp = await context.newCDPSession(page);
    await cdp.send('WebAuthn.enable');
    await cdp.send('WebAuthn.addVirtualAuthenticator', {
      options: {
        protocol: 'ctap2',
        transport: 'internal',
        hasResidentKey: true,
        hasUserVerification: true,
        isUserVerified: true,
        automaticPresenceSimulation: true,
      },
    });
  }
  return { context, page };
}

async function settle(page) {
  await page.waitForLoadState('networkidle');
  await page.evaluate(async () => {
    await document.fonts.ready;
    await Promise.all(
      [...document.images].map((img) =>
        img.complete ? null : new Promise((done) => { img.onload = img.onerror = done; }),
      ),
    );
  });
}

// The theme is the system's, as for a reader whose browser says dark or light
// (no `umbral_theme` cookie: the control then says "Following your system.").
// Switching it on the page keeps what a reload would lose (the app's secret).
async function setTheme(page, theme) {
  await page.emulateMedia({ colorScheme: theme });
  await settle(page);
  const note = page.locator('[data-theme-note]');
  if ((await note.count()) > 0 && !(await note.first().innerText()).includes('Following your system')) {
    throw new Error(`the theme control does not follow the system: "${await note.first().innerText()}"`);
  }
  return page.evaluate(() => getComputedStyle(document.body).backgroundColor);
}

// The clip of a frame: the union of the boxes of its selectors, padded, in page
// coordinates, clamped to the page.
async function clipOf(page, clip) {
  const boxes = [];
  for (const selector of clip.selectors) {
    const locator = page.locator(selector);
    if ((await locator.count()) === 0) throw new Error(`the clip selector matches nothing: ${selector}`);
    boxes.push(
      await locator.first().evaluate((el) => {
        const r = el.getBoundingClientRect();
        return { left: r.left + scrollX, top: r.top + scrollY, right: r.right + scrollX, bottom: r.bottom + scrollY };
      }),
    );
  }
  const pad = typeof clip.padding === 'number'
    ? { top: clip.padding, right: clip.padding, bottom: clip.padding, left: clip.padding }
    : clip.padding;
  const page_ = await page.evaluate(() => ({
    width: document.documentElement.scrollWidth,
    height: document.documentElement.scrollHeight,
  }));
  const left = Math.max(0, Math.min(...boxes.map((b) => b.left)) - pad.left);
  const top = Math.max(0, Math.min(...boxes.map((b) => b.top)) - pad.top);
  const right = Math.min(page_.width, Math.max(...boxes.map((b) => b.right)) + pad.right);
  const bottom = Math.min(page_.height, Math.max(...boxes.map((b) => b.bottom)) + pad.bottom);
  return { x: left, y: top, width: right - left, height: bottom - top };
}

async function shoot(page, name) {
  const frame = FRAMES[name];
  if (!frame) throw new Error(`no frame for ${name}`);
  await page.setViewportSize({ width: frame.width, height: frame.height });
  const backgrounds = {};
  for (const theme of THEMES) {
    backgrounds[theme] = await setTheme(page, theme);
    const options = { path: `${rawDir}/${name}-${theme}.png`, animations: 'disabled', caret: 'hide', fullPage: true };
    if (frame.clip) options.clip = await clipOf(page, frame.clip);
    await page.screenshot(options);
  }
  // The two themes must really differ, or the emulation did nothing.
  if (backgrounds.dark === backgrounds.light) {
    throw new Error(`${name}: the page looks the same in both themes (body background ${backgrounds.dark})`);
  }
  log(`captured ${name}`);
}

// What a failing step prints: where we are and what the page says, with long
// tokens (links, ids) blanked.
async function describe(page) {
  try {
    const text = (await page.locator('body').innerText()).replace(/\s+\n/g, '\n').replace(/[A-Za-z0-9_-]{24,}/g, '<token>');
    const controls = await page.locator('input, select, textarea, button, a').evaluateAll((els) =>
      els.map((e) => `${e.tagName.toLowerCase()}[type=${e.type ?? ''} id=${e.id} name=${e.name ?? ''}] ${(e.labels?.[0]?.innerText || e.innerText || '').replace(/\s+/g, ' ').slice(0, 60)}`),
    );
    return `url ${page.url().replace(/[A-Za-z0-9_-]{24,}/g, '<token>')}\n${text.slice(0, 1800)}\n--\n${controls.join('\n').slice(0, 2500)}`;
  } catch (error) {
    return `(could not describe the page: ${error.message})`;
  }
}

let current;
async function step(title, page, fn) {
  current = { title, page };
  log(`== ${title}`);
  return fn();
}

async function signInAsOperator(page) {
  await page.goto(`${base}/admin`);
  await page.locator('[data-screen="operator-signin"]').waitFor();
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await page.waitForURL(/\/admin\/connectors/);
  await settle(page);
}

async function saveCredentials(page, kind, label) {
  await page.goto(`${base}/admin/connectors/${kind}`);
  await page.locator('#clientId').fill('dummy-client-id');
  await page.locator('#clientSecret').fill('dummy-client-secret');
  await page.getByRole('button', { name: 'Save connector' }).click();
  await page.waitForURL(new RegExp(`/admin/connectors/${kind}\\?msg=`));
  await settle(page);
  log(`${label} saved`);
}

// ---------------------------------------------------------------------------
// The walk
// ---------------------------------------------------------------------------
try {
  const { context: operatorContext, page: operatorPage } = await newContext(
    { width: operator.width, height: operator.height },
    { passkeys: true },
  );
  const op = operatorPage;

  await step('operator signs in', op, () => signInAsOperator(op));

  // The fake connector is a development tool of the harness in samples/test,
  // never to appear in a picture.
  await step('guard: no fake connector', op, async () => {
    await op.goto(`${base}/admin/connectors/new`);
    const kinds = await op.locator('#add-kind option').evaluateAll((options) => options.map((o) => o.value));
    if (kinds.includes('fake')) {
      throw new Error('the Provider list offers "fake": this instance runs with UMBRAL__ALLOW_FAKE_CONNECTOR, which must never be set for the screenshots');
    }
  });

  // 1. GitHub chosen in the list, nothing saved yet.
  await step('connector-github', op, async () => {
    await op.goto(`${base}/admin/connectors/github`);
    await op.getByText('Redirect URI to register with the provider', { exact: false }).first().waitFor();
    const callback = await op.locator('#callback-github').inputValue();
    if (callback !== `${base}/connect/github/callback`) throw new Error(`unexpected redirect URI: ${callback}`);
    await shoot(op, 'connector-github');
  });

  // 2. Dummy credentials: a sign-in button shows without a working provider.
  await step('GitHub credentials', op, () => saveCredentials(op, 'github', 'GitHub'));

  // 3. The app, captured right after Create app (client ID and secret shown once).
  let clientId = '';
  let appUrl = '';
  await step('app-created', op, async () => {
    await op.goto(`${base}/admin/apps/new`);
    await op.getByLabel('Name', { exact: true }).fill(APP.name);
    await op.getByLabel('Redirect URI', { exact: true }).fill(APP.redirectUri);
    await op.getByLabel('Web application').check();
    await op.getByLabel('All shared connectors').first().check();
    await op.getByRole('button', { name: 'Create app' }).click();
    await op.waitForURL(/\/admin\/apps\/[^/?#]+\?msg=app-created/);
    await op.locator('[data-secret-once] input#revealed-secret').waitFor();
    const idInput = op.locator('input[id^="client-id-"]');
    clientId = await idInput.inputValue();
    if (!clientId) throw new Error('the app has no client ID');
    appUrl = op.url().split('?')[0];
    await shoot(op, 'app-created');
    // The picture must show both values whole (task 3694). Only the ID and
    // the URL are measured, never the secret.
    for (const selector of ['input[id^="client-id-"]', 'input[id^="issuer-"]']) {
      if (await op.locator(selector).evaluate((el) => el.scrollWidth > el.clientWidth)) {
        throw new Error(`${selector} cuts its value at ${FRAMES['app-created'].width}px`);
      }
    }
  });

  // 4. Google, Discord, a passkey connector and Fediverse.
  await step('Google', op, () => saveCredentials(op, 'google', 'Google'));
  await step('Discord', op, () => saveCredentials(op, 'discord', 'Discord'));
  await step('Passkey connector', op, async () => {
    await op.goto(`${base}/admin/connectors/passkey`);
    await op.getByLabel('Invitations only').check();
    await op.getByRole('button', { name: 'Enable passkeys' }).click();
    await op.waitForURL(/\/admin\/connectors\/passkey\?msg=/);
    await settle(op);
  });
  await step('Fediverse connector', op, async () => {
    await op.goto(`${base}/admin/connectors/fediverse`);
    await op.getByLabel('Any server').check();
    await op.getByRole('button', { name: 'Enable Fediverse' }).click();
    await op.waitForURL(/\/admin\/connectors\/fediverse\?msg=/);
    await settle(op);
  });

  // 5-7. The sign-in page of the app and the two steps behind its buttons, for
  // a browser that has never signed in (no "Last used" badge).
  const authorizeUrl = () => `${base}/connect/authorize?${authorizeQuery(clientId)}`;
  const visitor = await newContext({ width: person.width, height: person.height });
  const vp = visitor.page;
  await step('sign-in', vp, async () => {
    await vp.goto(authorizeUrl());
    await vp.locator('[data-screen="signin"]').waitFor();
    await vp.getByRole('heading', { name: `Sign in to ${APP.name}` }).waitFor();
    const buttons = await vp.locator('[data-screen="signin"] a, [data-screen="signin"] button').allInnerTexts();
    log(`buttons: ${buttons.map((b) => b.trim().replace(/\s+/g, ' ')).join(' | ')}`);
    await shoot(vp, 'sign-in');
  });

  await step('passkey-step', vp, async () => {
    await vp.getByRole('link', { name: /Sign in with a passkey/i }).or(vp.getByRole('button', { name: /Sign in with a passkey/i })).first().click();
    await vp.locator('[data-screen="passkey-signin"]').waitFor();
    await vp.getByRole('button', { name: 'Continue with your passkey' }).waitFor();
    await shoot(vp, 'passkey-step');
  });

  await step('fediverse-step', vp, async () => {
    await vp.goto(authorizeUrl());
    await vp.locator('[data-screen="signin"]').waitFor();
    await vp.getByRole('link', { name: /Sign in with Fediverse/i }).or(vp.getByRole('button', { name: /Sign in with Fediverse/i })).first().click();
    await vp.getByText('Where is your account?').waitFor();
    await vp.locator('input[type="text"], input[type="url"], input:not([type])').first().fill('@ana@social.example');
    await vp.evaluate(() => document.activeElement?.blur());
    await shoot(vp, 'fediverse-step');
  });
  await visitor.context.close();

  // 8. A person with a passkey, through an invitation.
  let invitationUrl = '';
  await step('invitation', op, async () => {
    await op.goto(`${base}/admin/connectors/passkey`);
    await op.locator('#note').fill('Sam');
    await op.getByRole('button', { name: 'Invite someone' }).click();
    await op.waitForLoadState('networkidle');
    const link = await op.locator('input[value^="http"]').evaluateAll((els) => els.map((e) => e.value).find((v) => v.startsWith(location.origin) && !v.includes('/connect/') ));
    if (!link) throw new Error('no invitation link on the page after Invite someone');
    invitationUrl = link;
  });

  await step('your-passkeys', undefined, async () => {
    const person1 = await newContext({ width: person.width, height: person.height }, { passkeys: true });
    const pp = person1.page;
    current.page = pp;
    await pp.goto(invitationUrl);
    await pp.getByLabel('Your name').fill('Sam Rivera');
    await pp.getByLabel(/passkey/i).first().fill('Phone');
    await pp.getByRole('button', { name: 'Create my passkey' }).click();
    await pp.getByRole('heading', { name: 'Your passkeys' }).waitFor({ timeout: 30000 });
    // The page as the person finds it later: without the notice of the redirect.
    await pp.goto(`${base}/account/passkeys`);
    await pp.getByRole('heading', { name: 'Your passkeys' }).waitFor();
    await pp.getByText('Phone', { exact: true }).waitFor();
    await settle(pp);
    await shoot(pp, 'your-passkeys');
    await person1.context.close();
  });

  // 9. The operator adds a passkey of their own.
  await step('operator-credential', op, async () => {
    await op.goto(`${base}/admin/credential`);
    await op.locator('#name').fill('Laptop');
    await op.locator('#current-add').fill(password);
    await op.getByRole('button', { name: 'Add a passkey' }).click();
    await op.waitForURL(/\/admin\/credential\?msg=operator-passkey-added/, { timeout: 30000 });
    // Steady state: the page again, without the "Passkey added." notice and
    // with the form empty.
    await op.goto(`${base}/admin/credential`);
    await op.locator('[data-section="passkeys"]').getByText('Laptop', { exact: true }).waitFor();
    await settle(op);
    await shoot(op, 'operator-credential');
  });

  // 10. Branding: the accent colour and a logo drawn here.
  await step('sign-in-branded', op, async () => {
    await op.goto(appUrl);
    await op.locator('input[id^="accent-"]').fill(ACCENT);
    await op.getByRole('button', { name: 'Save colour' }).click();
    await op.waitForURL(/msg=app-accent-saved/);
    await settle(op);

    const logo = await op.evaluate((accent) => {
      const canvas = document.createElement('canvas');
      canvas.width = canvas.height = 256;
      const g = canvas.getContext('2d');
      // A jar on a transparent background, so it reads as a logo on the dark
      // and the light card, and not as one more button in the accent.
      g.fillStyle = accent; // the jar
      g.beginPath();
      g.roundRect(48, 84, 160, 156, 34);
      g.fill();
      g.fillStyle = '#f2a65a'; // its lid
      g.beginPath();
      g.roundRect(66, 20, 124, 50, 14);
      g.fill();
      g.fillStyle = '#fff4e6'; // its label
      g.beginPath();
      g.roundRect(78, 126, 100, 72, 14);
      g.fill();
      return canvas.toDataURL('image/png').split(',')[1];
    }, ACCENT);
    await op.locator('input[type="file"][name="logo"]').setInputFiles({
      name: 'larder.png',
      mimeType: 'image/png',
      buffer: Buffer.from(logo, 'base64'),
    });
    // JavaScript is on: the crop UI appears with the whole image chosen; Save
    // logo sends the cropped PNG. Clicking before it is there would post the
    // raw file and the page would reject it.
    await op.locator('[data-logo-crop] img.um-crop-image').waitFor();
    await op.getByRole('button', { name: 'Save logo' }).click();
    await op.waitForURL(/msg=app-logo-saved/);
    await settle(op);

    const branded = await newContext({ width: person.width, height: person.height });
    current.page = branded.page;
    await branded.page.goto(authorizeUrl());
    await branded.page.locator('[data-screen="signin"]').waitFor();
    await branded.page.locator(`[data-screen="signin"] img[alt="${APP.name}"]`).waitFor();
    await shoot(branded.page, 'sign-in-branded');
    await branded.context.close();
  });

  await operatorContext.close();
  log('== all screens captured');
} catch (error) {
  console.error(`\ncapture failed at "${current?.title}": ${error.message}`);
  if (current?.page) {
    console.error(await describe(current.page));
    try {
      // Never a picture of a page that shows the app's secret, whatever step failed.
      if ((await current.page.locator('[data-secret-once]').count()) === 0) {
        await current.page.screenshot({ path: `${rawDir}/_failure.png`, fullPage: true });
      }
    } catch {
      // the page may be gone
    }
  }
  await browser.close();
  process.exit(1);
}
await browser.close();
