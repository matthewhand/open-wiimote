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
