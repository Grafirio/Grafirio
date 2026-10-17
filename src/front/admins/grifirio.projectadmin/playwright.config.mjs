import { defineConfig, devices } from '@playwright/test';

const port = Number(process.env.GRAFIRIO_BROWSER_PORT || 4179);
if (!Number.isInteger(port) || port < 1024 || port > 65535) throw new Error('Invalid browser test port');
export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.spec.mjs',
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  use: {
    ...devices['Desktop Chrome'],
    baseURL: `http://127.0.0.1:${port}`,
    serviceWorkers: 'block',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  webServer: {
    command: `node node_modules/vite/bin/vite.js --config e2e/vite.config.mjs --host 127.0.0.1 --port ${port} --strictPort`,
    url: `http://127.0.0.1:${port}`,
    reuseExistingServer: false,
    timeout: 30_000,
  },
});
