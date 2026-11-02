import { defineConfig } from '@playwright/test';
import fs from 'node:fs';
import { randomInt } from 'node:crypto';
import os from 'node:os';
import path from 'node:path';

// Playwright evaluates the config again in test workers. Carry the run-specific
// values through the inherited environment so webServer and workers agree.
const inheritedPort = process.env.OPENWIIMOTE_PLAYWRIGHT_PORT;
const PORT = inheritedPort === undefined ? randomInt(20_000, 60_000) : Number(inheritedPort);
if (!Number.isSafeInteger(PORT) || PORT < 20_000 || PORT >= 60_000) {
  throw new Error('Invalid Playwright test-only port');
}
process.env.OPENWIIMOTE_PLAYWRIGHT_PORT = String(PORT);
const TOKEN = 'playwright-token';

// A private directory per invocation prevents cleanup from touching any other
// run's synthetic profiles. Reused only by workers spawned within this run.
const inheritedHome = process.env.OPENWIIMOTE_PLAYWRIGHT_CONFIG_HOME;
if (inheritedHome && !fs.existsSync(inheritedHome)) {
  throw new Error('Playwright test profile home must be a private run-owned temp directory');
}
export const CONFIG_HOME = inheritedHome
  ? fs.realpathSync(inheritedHome)
  : fs.mkdtempSync(path.join(os.tmpdir(), 'openwiimote-playwright-'));
if (path.dirname(CONFIG_HOME) !== fs.realpathSync(os.tmpdir()) ||
    !path.basename(CONFIG_HOME).startsWith('openwiimote-playwright-') ||
    (fs.statSync(CONFIG_HOME).mode & 0o077) !== 0) {
  throw new Error('Playwright test profile home must be a private run-owned temp directory');
}
process.env.OPENWIIMOTE_PLAYWRIGHT_CONFIG_HOME = CONFIG_HOME;

export default defineConfig({
  testDir: './tests/OpenWiimote.App',
  outputDir: path.join(CONFIG_HOME, 'test-results'),
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  workers: 1,
  reporter: 'list',
  use: {
    baseURL: `http://127.0.0.1:${PORT}`
  },
  webServer: {
    command: `dotnet run --project src/OpenWiimote.App/OpenWiimote.App.csproj -c Release -- --headless --urls http://127.0.0.1:${PORT}`,
    url: `http://127.0.0.1:${PORT}/api/health`,
    timeout: 180_000,
    reuseExistingServer: false, // Fail closed if the random port is already occupied.
    env: {
      OPENWIIMOTE_ACCESS_TOKEN: TOKEN,
      XDG_CONFIG_HOME: CONFIG_HOME
    }
  }
});
