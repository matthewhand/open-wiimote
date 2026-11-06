/**
 * Device search/filter tests.
 *
 * The page is the real index.html served by the real headless service; only the
 * /api/state response is stubbed so the tests can present two connected remotes
 * without owning two Wiimotes.
 */

import type { Page } from '@playwright/test';
import { expect, test } from './support/fixtures';
import { TOKEN, device, stateWith } from './support/state';

async function openWithDevices(page: Page, devices: unknown[]) {
  await page.route('**/api/state', (route) => route.fulfill({ json: stateWith(devices) }));
  await page.goto(`/?token=${TOKEN}`);
  await expect(page.locator('#app')).toBeVisible();
}

const TWO_DEVICES = [
  device('device-1', 'Living room', '02:11:AB:33:CD:55', 85),
  device('device-2', 'Kitchen', '00:1E:35:11:22:33', 42)
];

test.describe('device search/filter', () => {
  test('enables the filter when two remotes are connected', async ({ page }) => {
    await openWithDevices(page, TWO_DEVICES);

    const search = page.locator('#deviceSearch');
    await expect(search).toBeEnabled();
    await expect(page.locator('#deviceList .device')).toHaveCount(2);
  });

  test('filters by device name', async ({ page }) => {
    await openWithDevices(page, TWO_DEVICES);

    await page.locator('#deviceSearch').fill('kitchen');

    await expect(page.locator('#deviceList .device')).toHaveCount(1);
    await expect(page.locator('#deviceList .device')).toContainText('Kitchen');
  });

  test('filters by Bluetooth address and restores every remote when cleared', async ({ page }) => {
    await openWithDevices(page, TWO_DEVICES);

    const search = page.locator('#deviceSearch');
    await search.fill('CD:55');
    await expect(page.locator('#deviceList .device')).toHaveCount(1);
    await expect(page.locator('#deviceList .device')).toContainText('Living room');

    await search.fill('');
    await expect(page.locator('#deviceList .device')).toHaveCount(2);
  });

  test('shows no remotes when the filter matches nothing', async ({ page }) => {
    await openWithDevices(page, TWO_DEVICES);

    await page.locator('#deviceSearch').fill('nunchuk');

    await expect(page.locator('#deviceList .device')).toHaveCount(0);
  });

  test('keeps the filter disabled with a single remote', async ({ page }) => {
    await openWithDevices(page, [TWO_DEVICES[0]]);

    await expect(page.locator('#deviceList .device')).toHaveCount(1);
    await expect(page.locator('#deviceSearch')).toBeDisabled();
  });

  test('shows the profile a remote is pinned to', async ({ page }) => {
    const payload = stateWith(TWO_DEVICES);
    payload.profiles = ['Default', 'Racing'];
    payload.settings.deviceProfiles = { 'device-1': 'Racing' };
    await page.route('**/api/state', (route) => route.fulfill({ json: payload }));

    await page.goto(`/?token=${TOKEN}`);
    await expect(page.locator('#app')).toBeVisible();
    await page.locator('#deviceList .device').first().click();

    await expect(page.locator('#deviceProfile')).toHaveValue('Racing');
  });

  test('sends the profile picked for the selected remote', async ({ page }) => {
    const payload = stateWith(TWO_DEVICES);
    payload.profiles = ['Default', 'Racing'];
    const sent: Array<{ deviceKey: string; profile: string | null }> = [];

    await page.route('**/api/state', (route) => route.fulfill({ json: payload }));
    await page.route('**/api/deviceprofile', (route) => {
      const body = route.request().postDataJSON();
      sent.push(body);
      // Answer like the service does, so the picker is checked against what it reports back.
      payload.settings.deviceProfiles = body.profile ? { [body.deviceKey]: body.profile } : {};
      route.fulfill({ json: payload });
    });

    await page.goto(`/?token=${TOKEN}`);
    await expect(page.locator('#app')).toBeVisible();
    await page.locator('#deviceList .device').first().click();

    // The picker starts on the active profile, so picking one pins the remote to it.
    await expect(page.locator('#deviceProfile')).toHaveValue('');
    await page.locator('#deviceProfile').selectOption('Racing');
    await expect.poll(() => sent.length).toBe(1);
    expect(sent[0]).toEqual({ deviceKey: 'device-1', profile: 'Racing' });
    await expect(page.locator('#deviceProfile')).toHaveValue('Racing');

    // And going back to the active profile clears the assignment.
    await page.locator('#deviceProfile').selectOption('');
    await expect.poll(() => sent.length).toBe(2);
    expect(sent[1]).toEqual({ deviceKey: 'device-1', profile: null });
  });

  test('keeps the filter while the page polls a fresh snapshot', async ({ page }) => {
    // The battery of the second remote changes between polls, which rebuilds the rows.
    const refreshed = [TWO_DEVICES[0], device('device-2', 'Kitchen', '00:1E:35:11:22:33', 39)];
    let polls = 0;
    await page.route('**/api/state', (route) =>
      route.fulfill({ json: stateWith(polls++ === 0 ? TWO_DEVICES : refreshed) })
    );

    await page.goto(`/?token=${TOKEN}`);
    await expect(page.locator('#app')).toBeVisible();
    await expect(page.locator('#deviceList .device')).toHaveCount(2);

    await page.locator('#deviceSearch').fill('kitchen');
    await expect(page.locator('#deviceList .device')).toHaveCount(1);

    await expect(page.locator('#deviceList .device')).toContainText('39%');
    await expect(page.locator('#deviceList .device')).toHaveCount(1);
    await expect(page.locator('#deviceCount')).toContainText('1 of 2');
  });
});
