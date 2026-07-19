import { defineConfig, devices } from '@playwright/test';

const externalBaseUrl = process.env['PLAYWRIGHT_BASE_URL'];

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.visual.spec.ts',
  fullyParallel: false,
  workers: 1,
  reporter: 'line',
  outputDir: 'test-results/playwright',
  use: {
    baseURL: externalBaseUrl ?? 'http://127.0.0.1:4200',
    locale: 'ar-EG',
    colorScheme: 'light',
    trace: 'retain-on-failure'
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: externalBaseUrl ? undefined : {
    command: 'npm run start:web',
    url: 'http://127.0.0.1:4200',
    reuseExistingServer: true,
    timeout: 120_000
  }
});
