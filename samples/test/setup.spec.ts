// Half one of the harness: claim is done by run.sh (reset-credential); this
// signs in as the operator, adds the fake connector, registers the sample as
// an app and writes its client ID and secret to out/app.env for run.sh to hand
// to the sample. Neither the password nor the secret is ever logged.
import { test, expect } from '@playwright/test';
import { mkdirSync, writeFileSync } from 'node:fs';

function need(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`${name} is not set`);
  return value;
}

test('claim, add the fake connector, register the app', async ({ page }) => {
  const sample = need('SAMPLE');
  const samplePort = need('SAMPLE_PORT');
  const appType = need('APP_TYPE');
  const redirectPath = need('REDIRECT_PATH');
  const postLogoutPath = need('POST_LOGOUT_PATH');
  const password = need('ADMIN_PASSWORD');
  const origin = `http://localhost:${samplePort}`;

  // Sign in as the operator.
  await page.goto('/admin');
  await expect(page.locator('[data-screen="operator-signin"]')).toBeVisible();
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/\/admin\/connectors/);

  // Add the fake connector through the add form (the kind is offered only
  // because of the Development and allow-fake-connector settings of the
  // harness's Umbral).
  await page.goto('/admin/connectors/new');
  await page.getByLabel('Provider', { exact: true }).selectOption('fake');
  await page.getByLabel('Identifier', { exact: true }).fill('fake');
  await page.getByLabel('Button label', { exact: true }).fill('Fake (development)');
  await page.getByLabel('Client ID', { exact: true }).fill('fake');
  await page.getByLabel('Client secret', { exact: true }).fill('fake-secret');
  await page.getByRole('button', { name: 'Add connector' }).click();
  await expect(page).toHaveURL(/\/admin\/connectors\/fake(\?|$)/);

  // Register the sample as an app.
  await page.goto('/admin/apps/new');
  await page.getByLabel('Name', { exact: true }).fill(sample);
  await page.getByLabel('Redirect URI', { exact: true }).fill(`${origin}${redirectPath}`);
  await page
    .getByLabel('Post-logout redirect URI (optional)', { exact: true })
    .fill(`${origin}${postLogoutPath}`);
  if (appType === 'web') {
    await page.getByLabel('Web application').check();
  } else if (appType === 'spa') {
    await page.getByLabel('Single-page or native app').check();
  } else {
    throw new Error(`APP_TYPE must be web or spa, not "${appType}"`);
  }
  await page.getByRole('button', { name: 'Create app' }).click();
  await expect(page).toHaveURL(/\/admin\/apps\/[^/?#]+/);

  const clientIdInput = page.locator('input[id^="client-id-"]');
  await expect(clientIdInput).toBeVisible();
  const clientId = await clientIdInput.inputValue();
  expect(clientId).not.toBe('');

  let clientSecret = '';
  if (appType === 'web') {
    const secretInput = page.locator('[data-secret-once] input#revealed-secret');
    await expect(secretInput).toBeVisible();
    clientSecret = await secretInput.inputValue();
    expect(clientSecret).not.toBe('');
  }

  mkdirSync('out', { recursive: true });
  writeFileSync(
    'out/app.env',
    `UMBRAL_CLIENT_ID=${clientId}\nUMBRAL_CLIENT_SECRET=${clientSecret}\n`,
    { mode: 0o600 },
  );
});
