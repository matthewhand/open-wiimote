import { expect, test } from '@playwright/test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

test('uses a private profile home and a non-shared test server', async ({ request }, info) => {
  const configHome = process.env.OPENWIIMOTE_PLAYWRIGHT_CONFIG_HOME;
  expect(configHome, 'runner must provide its run-owned XDG home').toBeTruthy();
  const resolved = fs.realpathSync(configHome!);
  expect(path.dirname(resolved)).toBe(fs.realpathSync(os.tmpdir()));
  expect(path.basename(resolved)).toMatch(/^openwiimote-playwright-/);
  expect(fs.statSync(resolved).mode & 0o077, 'test home must be private').toBe(0);
  const serverUrl = new URL(info.project.use.baseURL!);
  expect(serverUrl.hostname).toBe('127.0.0.1');
  expect(serverUrl.port, 'old global port must not be reused').not.toBe('8199');
  expect((await request.get('/api/health')).status()).toBe(200);
  console.log('ISOLATION_RECEIPT ' + JSON.stringify({ configHome: resolved, port: Number(serverUrl.port) }));
});
