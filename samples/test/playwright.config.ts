import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  timeout: 60_000,
  retries: 0,
  workers: 1,
  fullyParallel: false,
  reporter: 'list',
  use: {
    trace: 'retain-on-failure',
    baseURL: process.env.UMBRAL_URL,
  },
  outputDir: 'out/test-results',
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
});
