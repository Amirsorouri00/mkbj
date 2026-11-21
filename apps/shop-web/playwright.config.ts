import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.e2e.ts',
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: 'list',
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:3000',
    channel: 'chrome',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'desktop-chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'phone-375', use: { ...devices['iPhone 13 Mini'], browserName: 'chromium', channel: 'chrome', viewport: { width: 375, height: 812 } } },
    { name: 'phone-landscape', use: { ...devices['iPhone 13 Mini landscape'], browserName: 'chromium', channel: 'chrome', viewport: { width: 812, height: 375 }, reducedMotion: 'reduce' } },
  ],
  webServer: process.env.E2E_EXTERNAL_SERVER ? [] : [{
    command: 'pnpm dev --hostname localhost',
    // Probe a static asset so API warm-up and catalog data never masquerade as a web-server
    // startup failure.
    url: 'http://localhost:3000/silent-check-sso.html',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  }],
});
