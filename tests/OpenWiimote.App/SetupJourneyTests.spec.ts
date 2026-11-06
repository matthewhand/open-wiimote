import { expect, test } from './support/fixtures';
import { AUTH, TOKEN, device, stateWith } from './support/state';

test('reports the experimental gyro stub without promising available readings', async ({ request }) => {
  const snapshot = await (await request.get('/api/state', { headers: AUTH })).json();
  expect(snapshot.motionPlus).toMatchObject({ status: 'experimental', available: false });
});

test('setup guidance follows backend, output and connection recovery', async ({ page }) => {
  let payload = { ...stateWith([]), gamepadAvailable: false, gamepadError: 'Permission denied' };
  await page.route('**/api/state', route => route.fulfill({ json: payload }));
  await page.goto(`/?token=${TOKEN}`);
  await expect(page.locator('#setupStatus')).toHaveText('Controller setup needed');
  await expect(page.locator('#setupController')).toContainText('check-linux-setup.sh');
  payload = { ...stateWith([]), gamepadAvailable: true, gamepadError: null };
  await expect(page.locator('#setupStatus')).toHaveText('Controller emulation is off');
  payload = { ...payload, emulationEnabled: true, devices: [device('a', 'Remote', '', 80)] } as typeof payload;
  await expect(page.locator('#setupStatus')).toHaveText('Waiting for controller output');
  payload = { ...payload, devices: [{ ...device('a', 'Remote', '', 80), virtualControllerAttached: true }] } as typeof payload;
  await expect(page.locator('#setupStatus')).toHaveText('Ready for games');
  await page.route('**/api/state', route => route.abort());
  await expect(page.locator('#setupStatus')).toContainText('Service connection lost');
  await page.unroute('**/api/state');
  await page.route('**/api/state', route => route.fulfill({ json: payload }));
  await expect(page.locator('#setupStatus')).toHaveText('Ready for games');
});

test('setup shortcut opens controller settings on a narrow screen', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.route('**/api/state', route => route.fulfill({ json: stateWith([]) }));
  await page.goto(`/?token=${TOKEN}`);
  await page.locator('#setupProfiles').click();
  await expect(page.locator('#emulationToggle')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('default profile deletion is rejected without losing the fallback', async ({ request }) => {
  const response = await request.post('/api/profile', { headers: AUTH, data: { name: 'Default', delete: true } });
  expect(response.status()).toBe(400);
  expect(await (await request.get('/api/profiles', { headers: AUTH })).json()).toContain('Default');
});


test('Linux wake guidance follows auto-connect and does not promise Bluetooth reconnection on other platforms', async ({ page }) => {
  let payload = stateWith([]);
  await page.route('**/api/state', route => route.fulfill({ json: payload }));
  await page.goto(`/?token=${TOKEN}`);
  await expect(page.locator('#setupRemote')).toContainText('1 + 2');
  await expect(page.locator('#autoConnectNote')).toContainText('paired, trusted');
  payload = { ...payload, settings: { ...payload.settings, autoConnect: false } };
  await expect(page.locator('#setupRemote')).toContainText('Enable Auto-connect');
  payload = { ...payload, platform: 'Windows', settings: { ...payload.settings, autoConnect: true } };
  await expect(page.locator('#autoConnectNote')).toContainText('connected by your operating system');
  await expect(page.locator('#setupRemote')).not.toContainText('15 seconds');
});

test('remembered player number stays distinct from a temporary LED pattern and updates without reselection', async ({ page }) => {
  let remote = { ...device('second', 'Second remote', ''), playerNumber: 2, ledMask: 16 };
  await page.route('**/api/state', route => route.fulfill({ json: stateWith([remote]) }));
  await page.goto(`/?token=${TOKEN}`);
  await expect(page.locator('#deviceListCard .device-side')).toContainText('Player 2');
  remote = { ...remote, playerNumber: 3 };
  await expect(page.locator('#deviceListCard .device-side')).toContainText('Player 3');
});
