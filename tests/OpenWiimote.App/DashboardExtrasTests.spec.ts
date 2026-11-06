/**
 * Dashboard interaction tests: the view switches, the tilt calibration toggle and the
 * extension panel.
 *
 * A single remote is stubbed into /api/state so the avatar and the details panel have
 * something to draw; the routes that store a setting are stubbed too, so a test never
 * depends on what an earlier test saved.
 */

import type { Page } from '@playwright/test';
import { expect, test } from './support/fixtures';
import { TOKEN, device, stateWith } from './support/state';

async function openDashboard(page: Page, devices: unknown[]) {
  const payload = stateWith(devices);
  await page.route('**/api/state', (route) => {
    for (const remote of payload.devices as any[]) remote.state.lastReport = new Date().toISOString();
    return route.fulfill({ json: payload });
  });
  await page.route('**/api/settings', (route) => route.fulfill({ json: payload }));
  await page.goto(`/?token=${TOKEN}`);
  await expect(page.locator('#app')).toBeVisible();
}

test('tilt calibration and extras toggles run without errors', async ({ page }) => {
  await openDashboard(page, [device('device-1', 'Remote', '02:11:AB:33:CD:55', 70)]);

  await page.locator('#viewOptions summary').click();

  for (let i = 0; i < 6; i++) await page.waitForResponse(r => r.url().endsWith('/api/state'));
  await page.locator('#tiltCalibration').check();
  await expect(page.locator('#tiltCalibrationNote')).toBeVisible();
  await page.locator('#tiltCalibration').uncheck();
  await expect(page.locator('#tiltCalibrationNote')).toBeHidden();

  for (const id of ['avatarRain', 'sparklineEnabled', 'keyboardShortcuts', 'rumbleShake', 'batterySegments']) {
    await page.locator('#' + id).click();
  }

  await page.locator('[data-tab="profiles"]').click();
  await page.locator('[data-tab="logs"]').click();
  await page.locator('[data-tab="dashboard"]').click();

  // The dev tilt overrides: capture a rest pose and pick a different axis for the model.
  await page.locator('#tiltOverrides summary').click();
  await page.locator('#devCaptureRest').click();
  await page.locator('#devMapY').selectOption('-x');
  await expect(page.locator('#devReadout')).toContainText('model');
  await page.locator('#devResetOverrides').click();
  await expect(page.locator('#devMapY')).toHaveValue('y');
});

test('the view switches reach the panels they name', async ({ page }) => {
  await openDashboard(page, [device('device-1', 'Remote', '02:11:AB:33:CD:55', 70)]);
  await page.locator('#viewOptions summary').click();

  // The sparkline switch hides the sparkline, not the IR view next to it.
  await page.locator('#sparklineEnabled').uncheck();
  await expect(page.locator('#sparkWrap')).toBeHidden();
  await page.locator('#sparklineEnabled').check();
  await expect(page.locator('#sparkWrap')).toBeVisible();

  // The IR panel only appears while the camera is switched on.
  await expect(page.locator('#irWrap')).toBeHidden();
  await page.locator('#irEnabled').check();
  await expect(page.locator('#irWrap')).toBeVisible();
});

test.describe('extension panel', () => {
  const classicState = {
    extensionConnected: true,
    extensionType: 2,
    classicController: { buttonA: true, leftStickX: 0.5 }
  };

  test('shows the Classic Controller model instead of the Nunchuk', async ({ page }) => {
    await openDashboard(page, [device('device-1', 'Remote', '02:11:AB:33:CD:55', 70, classicState)]);

    await expect(page.locator('#classicWrap')).toBeVisible();
    await expect(page.locator('#nunchukWrap')).toBeHidden();
    await expect(page.locator('#extensionNote')).toContainText('Classic Controller');
  });

  test('shows the Nunchuk model and says so', async ({ page }) => {
    await openDashboard(page, [
      device('device-1', 'Remote', '02:11:AB:33:CD:55', 70, { extensionConnected: true, extensionType: 1 })
    ]);

    await expect(page.locator('#nunchukWrap')).toBeVisible();
    await expect(page.locator('#classicWrap')).toBeHidden();
    await expect(page.locator('#extensionNote')).toContainText('Nunchuk');
  });

  test('says so when the attached controller is not decoded', async ({ page }) => {
    await openDashboard(page, [
      device('device-1', 'Remote', '02:11:AB:33:CD:55', 70, { extensionConnected: true, extensionType: 4 })
    ]);

    await expect(page.locator('#guitarWrap')).toBeVisible();
    await expect(page.locator('#extensionNote')).toContainText('does not decode');
  });

  test('hides every model when the remote has no extension', async ({ page }) => {
    await openDashboard(page, [device('device-1', 'Remote', '02:11:AB:33:CD:55', 70)]);

    await expect(page.locator('#extensionNote')).toHaveText('No extension attached.');
    await expect(page.locator('#classicWrap')).toBeHidden();
    await expect(page.locator('#nunchukWrap')).toBeHidden();
  });

  test('the view option hides and shows the attached model', async ({ page }) => {
    await openDashboard(page, [
      device('device-1', 'Remote', '02:11:AB:33:CD:55', 70, {
        extensionConnected: true,
        extensionType: 1
      })
    ]);

    await page.locator('#viewOptions summary').click();
    await page.locator('#showExtensions').uncheck();
    await expect(page.locator('#nunchukWrap')).toBeHidden();

    await page.locator('#showExtensions').check();
    await expect(page.locator('#nunchukWrap')).toBeVisible();
  });
});
