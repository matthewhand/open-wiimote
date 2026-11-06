import { expect, test } from './support/fixtures';
import { TOKEN, device, stateWith } from './support/state';

test('device tabs keep their selection through new remotes and return to dashboard when one remains', async ({ page }) => {
  const first = device('first', 'First remote', '00:11:22:33:44:55');
  const second = device('second', 'Second remote', '00:11:22:33:44:66');
  let devices = [first, second];
  await page.route('**/api/state', route => route.fulfill({ json: stateWith(devices) }));
  await page.goto(`/?token=${TOKEN}`);
  await page.locator('#tabs [data-tab="device:second"]').click();
  await expect(page.locator('#detailName')).toHaveText('Second remote');
  devices = [first, second, device('third', 'Third remote', '00:11:22:33:44:77')];
  await expect(page.locator('#tabs [data-device-tab]')).toHaveCount(3);
  await expect(page.locator('#tabs [data-tab="device:second"]')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('#detailName')).toHaveText('Second remote');
  devices = [second];
  await expect(page.locator('#tabs [data-device-tab]')).toHaveCount(0);
  await expect(page.locator('#tabs [data-tab="dashboard"]')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('#deviceListCard')).toBeVisible();
  await expect(page.locator('#detailName')).toHaveText('Second remote');
});

test('changing the active profile reloads a focused tilt editor before it can save into another profile', async ({ page }) => {
  let payload = stateWith([]);
  await page.route('**/api/state', route => route.fulfill({ json: payload }));
  await page.goto(`/?token=${TOKEN}`);
  await page.getByRole('button', { name: 'Profiles', exact: true }).click();
  await page.locator('#maxTilt').fill('60');
  payload = { ...payload, activeProfile: 'Racing', profiles: ['Default', 'Racing'], tilt: { ...payload.tilt, maxTiltDegrees: 35 } };
  await expect(page.locator('#profileSelect')).toHaveValue('Racing');
  await expect(page.locator('#maxTilt')).toHaveValue('35');
});

test('avatar levelling does not change live sensor tilt or send profile edits', async ({ page }) => {
  const payload = stateWith([device('first', 'Remote', '', 80, {
    accelX: 0.5, accelY: 0, accelZ: 1, tiltXDegrees: 27, tiltYDegrees: 0
  })]);
  const mutations: string[] = [];
  await page.route('**/api/state', route => {
    payload.devices[0].state.lastReport = new Date().toISOString();
    return route.fulfill({ json: payload });
  });
  page.on('request', request => { if (request.method() === 'POST' && request.url().endsWith('/api/tilt')) mutations.push(request.url()); });
  await page.goto(`/?token=${TOKEN}`);
  await expect(page.locator('#detailTilt')).toHaveText('27° / 0°');
  await page.locator('#viewOptions summary').click();
  for (let i = 0; i < 6; i++) await page.waitForResponse(r => r.url().endsWith('/api/state'));
  await page.locator('#tiltCalibration').check();
  await expect(page.locator('#tiltCalibrationNote')).toContainText('virtual-controller output is unchanged');
  await expect(page.locator('#detailTilt')).toHaveText('27° / 0°');
  expect(mutations).toEqual([]);
});

test('an invalid state response clears readiness and recovers on the next valid snapshot', async ({ page }) => {
  const payload = { ...stateWith([{ ...device('first', 'Remote', ''), virtualControllerAttached: true }]), emulationEnabled: true };
  let valid = true;
  await page.route('**/api/state', route => route.fulfill({ json: valid ? payload : {} }));
  await page.goto(`/?token=${TOKEN}`);
  await expect(page.locator('#setupStatus')).toHaveText('Ready for games');
  valid = false;
  await expect(page.locator('#setupStatus')).toContainText('Service connection lost');
  valid = true;
  await expect(page.locator('#setupStatus')).toHaveText('Ready for games');
});

test('a hung state request times out instead of leaving a stale ready indicator', async ({ page }) => {
  const payload = { ...stateWith([{ ...device('first', 'Remote', ''), virtualControllerAttached: true }]), emulationEnabled: true };
  let hanging = false;
  await page.route('**/api/state', async route => {
    if (hanging) await new Promise(resolve => setTimeout(resolve, 6500));
    await route.fulfill({ json: payload }).catch(() => {}); // The client may already have aborted.
  });
  await page.goto(`/?token=${TOKEN}`);
  await expect(page.locator('#setupStatus')).toHaveText('Ready for games');
  hanging = true;
  await expect(page.locator('#setupStatus')).toContainText('Service connection lost', { timeout: 8000 });
});
