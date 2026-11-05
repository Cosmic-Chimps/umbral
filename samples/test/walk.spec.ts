// Half two of the harness: the contract's walk (samples/README.md), the same
// for every sample, against the sample's own address.
import { test, expect } from '@playwright/test';

test('the walk: public, private, sign in with the fake connector, sign out', async ({ page, request }) => {
  const port = process.env.SAMPLE_PORT;
  if (!port) throw new Error('SAMPLE_PORT is not set');
  const base = `http://localhost:${port}`;

  // The sample has just started: wait until it answers.
  await expect
    .poll(
      async () => {
        try {
          return (await request.get(`${base}/public`)).status();
        } catch {
          return 0;
        }
      },
      { timeout: 60_000, intervals: [500, 1000, 2000] },
    )
    .toBe(200);

  // The home page, signed out.
  await page.goto(`${base}/`);
  await expect(page.locator('h1')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Public', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Private', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Sign in', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Sign out' })).toHaveCount(0);

  // /public is open.
  await page.getByRole('link', { name: 'Public', exact: true }).click();
  await expect(page.locator('h1')).toHaveText('Public');

  // /private sends a signed-out visitor to Umbral.
  await page.goto(`${base}/private`);
  await expect(page.locator('[data-screen="signin"]')).toBeVisible();
  await page.locator('[data-connector="fake"]').click();
  await expect(page.locator('[data-screen="fake-provider"]')).toBeVisible();

  // An identity without an email.
  await page.getByLabel('Subject').fill('sample-person');
  await page.getByLabel('Email', { exact: true }).clear();
  await page.getByLabel('Display name').fill('Sample Person');
  await page.locator('button[data-fake-approve]').click();

  // Back on /private, with the claims.
  await expect(page).toHaveURL(/\/private$/);
  await expect(page.locator('h1')).toHaveText('Private');

  const valueOf = (claim: string) =>
    page
      .locator('tr')
      .filter({ has: page.locator('th', { hasText: new RegExp(`^\\s*${claim}\\s*$`) }) })
      .locator('td');

  await expect(valueOf('sub')).not.toBeEmpty();
  await expect(valueOf('name')).toHaveText('Sample Person');
  await expect(valueOf('idp')).toHaveText('fake');
  await expect(valueOf('email')).toHaveCount(0);
  await expect(valueOf('email_verified')).toHaveCount(0);

  // Sign out: the app's session, then Umbral's, then back on /.
  await page.goto(`${base}/`);
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(`${base}/`);
  await expect(page.getByRole('link', { name: 'Sign in', exact: true })).toBeVisible();

  // Umbral's session is gone too: /private sends us to Umbral again.
  await page.goto(`${base}/private`);
  await expect(page.locator('[data-screen="signin"]')).toBeVisible();
});
