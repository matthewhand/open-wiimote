/**
 * End-to-end tests for the headless web UI.
 *
 * These run against a real headless service (started by playwright.config.ts) with an
 * isolated XDG config home, so the page, the token login, the API and the profile files
 * are all the production code paths.
 */

import type { Page } from '@playwright/test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { expect, test } from './support/fixtures';
import { TOKEN, device, stateWith } from './support/state';

const AUTH = { Authorization: `Bearer ${TOKEN}` };

// Never fall back to a shared or real user profile location.
const RUN_CONFIG_HOME = process.env.OPENWIIMOTE_PLAYWRIGHT_CONFIG_HOME;
if (!RUN_CONFIG_HOME ||
    !path.isAbsolute(RUN_CONFIG_HOME) ||
    !fs.existsSync(RUN_CONFIG_HOME) ||
    path.dirname(fs.realpathSync(RUN_CONFIG_HOME)) !== fs.realpathSync(os.tmpdir()) ||
    !path.basename(RUN_CONFIG_HOME).startsWith('openwiimote-playwright-')) {
  throw new Error('Playwright did not supply a run-owned temporary profile directory');
}
const PROFILE_DIRECTORY = path.join(RUN_CONFIG_HOME, 'OpenWiimote', 'Profiles');

function removeProfile(name: string) {
  const file = path.join(PROFILE_DIRECTORY, name + '.json');
  if (fs.existsSync(file)) fs.rmSync(file);
}

/** Copies the Default profile under another name, like a user saving a new profile. */
function writeProfile(name: string) {
  removeProfile(name);
  fs.mkdirSync(PROFILE_DIRECTORY, { recursive: true });
  const profile = JSON.parse(fs.readFileSync(path.join(PROFILE_DIRECTORY, 'Default.json'), 'utf8'));
  profile.Name = name;
  fs.writeFileSync(path.join(PROFILE_DIRECTORY, name + '.json'), JSON.stringify(profile, null, 2));
}

async function openProfiles(page: Page) {
  await page.goto(`/?token=${TOKEN}`);
  await expect(page.locator('#app')).toBeVisible();
  await page.getByRole('button', { name: 'Profiles' }).click();
}

test.describe('headless web UI', () => {
  test.afterEach(async ({ request }) => {
    fs.mkdirSync(PROFILE_DIRECTORY, { recursive: true });
    for (const file of fs.readdirSync(PROFILE_DIRECTORY)) {
      if (file.endsWith('.json') && file !== 'Default.json') {
        fs.rmSync(path.join(PROFILE_DIRECTORY, file));
      }
    }

    await request.post('/api/profiles/reload', { headers: AUTH });
  });

  test('boots from a token link and renders the dashboard', async ({ page }) => {
    await page.goto(`/?token=${TOKEN}`);

    await expect(page.locator('#app')).toBeVisible();
    await expect(page.locator('#login')).toBeHidden();
    await expect(page.locator('#view')).toBeVisible();
    await expect(page.locator('#deviceListCard')).toBeVisible();
    await expect(page.locator('#deviceSearch')).toBeDisabled();

    await page.getByRole('button', { name: 'Profiles' }).click();
    await expect(page.locator('#profileCards .profile-card').first()).toBeVisible();
  });

  test('rejects a wrong token', async ({ page }) => {
    await page.goto('/?token=not-the-token');

    await expect(page.locator('#login')).toBeVisible();
    await expect(page.locator('#loginError')).not.toBeEmpty();
  });

  test('keeps the API behind the token', async ({ request }) => {
    expect((await request.get('/api/state')).status()).toBe(401);
    expect((await request.get('/api/state', { headers: AUTH })).status()).toBe(200);
  });

  test('action routes reject safe methods without changing state', async ({ request }) => {
    const before = await (await request.get('/api/state', { headers: AUTH })).json();
    for (const endpoint of ['/api/scan', '/api/disconnect', '/api/logs/clear',
      '/api/profiles/reload', '/api/emulation', '/api/settings']) {
      const response = await request.get(endpoint, { headers: AUTH });
      expect(response.status(), endpoint).toBe(405);
      expect(response.headers()['allow']).toBe('POST');
    }
    const after = await (await request.get('/api/state', { headers: AUTH })).json();
    expect(after.settings).toEqual(before.settings);
    expect(after.devices).toEqual(before.devices);
    expect((await request.post('/api/profiles/reload', { headers: AUTH })).status()).toBe(200);
  });

  test('malformed profile creation does not create a profile', async ({ request }) => {
    const before = await (await request.get('/api/profiles', { headers: AUTH })).json();
    const response = await request.post('/api/profiles', {
      headers: { ...AUTH, 'Content-Type': 'application/json' }, data: '{bad json'
    });
    expect(response.status()).toBe(400);
    expect(await (await request.get('/api/profiles', { headers: AUTH })).json()).toEqual(before);
  });

  test('opens without a token when the service does not ask for one', async ({ page }) => {
    await page.route('**/api/health', (route) =>
      route.fulfill({ json: { status: 'ok', authRequired: false } })
    );
    await page.route('**/api/state', (route) =>
      route.fulfill({ json: stateWith([device('device-1', 'Remote', '02:11:AB:33:CD:55', 70)]) })
    );

    await page.goto('/');

    await expect(page.locator('#app')).toBeVisible();
    await expect(page.locator('#login')).toBeHidden();
    // Without a token the page still has to poll, or the remote never shows up.
    await expect(page.locator('#deviceList .device')).toHaveCount(1);
  });

  test('creates a copy of the active profile from the New button', async ({ page, request }) => {
    await openProfiles(page);
    const before = await page.locator('.profile-card').count();

    await page.locator('#newProfile').click();

    await expect(page.locator('.profile-card', { hasText: 'New profile' })).toBeVisible();
    await expect(page.locator('.profile-card')).toHaveCount(before + 1);

    const profiles = await request.get('/api/profiles', { headers: AUTH });
    expect(await profiles.json()).toContain('New profile');
  });

  test('renames a profile from its card', async ({ page }) => {
    writeProfile('Rename Me');

    await openProfiles(page);
    const card = page.locator('.profile-card', { hasText: 'Rename Me' });
    await expect(card).toBeVisible();

    page.once('dialog', (dialog) => dialog.accept('Renamed Profile'));
    await card.getByRole('button', { name: 'Rename' }).click();

    await expect(page.locator('.profile-card', { hasText: 'Renamed Profile' })).toBeVisible();
    await expect(page.locator('.profile-card', { hasText: 'Rename Me' })).toHaveCount(0);
    expect(fs.existsSync(path.join(PROFILE_DIRECTORY, 'Renamed Profile.json'))).toBe(true);
    expect(fs.existsSync(path.join(PROFILE_DIRECTORY, 'Rename Me.json'))).toBe(false);
  });

  test('deletes a profile from its card', async ({ page }) => {
    writeProfile('Delete Me');

    await openProfiles(page);
    const card = page.locator('.profile-card', { hasText: 'Delete Me' });
    await expect(card).toBeVisible();

    page.once('dialog', (dialog) => dialog.accept());
    await card.getByRole('button', { name: 'Delete' }).click();

    await expect(page.locator('.profile-card', { hasText: 'Delete Me' })).toHaveCount(0);
    expect(fs.existsSync(path.join(PROFILE_DIRECTORY, 'Delete Me.json'))).toBe(false);
  });

  test('pins a profile to one remote and lets it follow the active profile again', async ({ request }) => {
    writeProfile('Racing');

    const assigned = await request.post('/api/deviceprofile', {
      headers: AUTH,
      data: { deviceKey: 'device-1', profile: 'Racing' }
    });
    expect(assigned.status()).toBe(200);

    const snapshot = await (await request.get('/api/state', { headers: AUTH })).json();
    expect(snapshot.settings.deviceProfiles['device-1']).toBe('Racing');

    const cleared = await request.post('/api/deviceprofile', {
      headers: AUTH,
      data: { deviceKey: 'device-1', profile: null }
    });
    expect(cleared.status()).toBe(200);

    const after = await (await request.get('/api/state', { headers: AUTH })).json();
    expect(after.settings.deviceProfiles['device-1']).toBeUndefined();
  });

  test('refuses a device profile that does not exist', async ({ request }) => {
    const response = await request.post('/api/deviceprofile', {
      headers: AUTH,
      data: { deviceKey: 'device-1', profile: 'No Such Profile' }
    });

    expect(response.status()).toBe(400);
    expect((await response.json()).error).toContain('not found');
  });

  test('saves a Wiimote button from the mapping dropdown', async ({ page }) => {
    await openProfiles(page);
    const select = page.locator('#mappings select').first();
    await expect(select).toBeVisible();
    const response = page.waitForResponse((result) =>
      result.url().includes('/api/mapping') && result.request().method() === 'POST');
    await select.selectOption('Home');
    expect((await response).status()).toBe(200);
    await expect(page.locator('#alert')).toBeHidden();

    await expect(page.locator('#mappings .map-row').first()).toContainText('A');
    const profile = JSON.parse(fs.readFileSync(path.join(PROFILE_DIRECTORY, 'Default.json'), 'utf8'));
    // Profiles store WiimoteButton as the numeric flag. Home is 0x80.
    expect(profile.A.WiimoteButton).toBe(128);
  });

  test('rejects invalid tilt numbers without altering a profile and persists valid values', async ({ page, request }) => {
    writeProfile('Tilt Validation');
    expect((await request.post('/api/profile', {
      headers: AUTH, data: { name: 'Tilt Validation' }
    })).status()).toBe(200);
    await openProfiles(page);

    const file = path.join(PROFILE_DIRECTORY, 'Tilt Validation.json');
    const before = fs.readFileSync(file, 'utf8');
    await page.locator('#maxTilt').fill('90');
    await page.locator('#sensitivity').fill('1.7');
    await page.locator('#deadZone').fill('0.14');
    await page.locator('#saveTilt').click();
    await expect(page.locator('#alert')).toContainText('Max tilt');
    expect(fs.readFileSync(file, 'utf8')).toBe(before);

    await page.locator('#maxTilt').fill('');
    await page.locator('#saveTilt').click();
    await expect(page.locator('#alert')).toContainText('Max tilt');
    expect(fs.readFileSync(file, 'utf8')).toBe(before);

    await page.locator('#maxTilt').fill('35');
    await page.locator('#sensitivity').fill('9');
    await page.locator('#saveTilt').click();
    await expect(page.locator('#alert')).toContainText('Sensitivity');
    expect(fs.readFileSync(file, 'utf8')).toBe(before);

    await page.locator('#sensitivity').fill('1.7');
    await page.locator('#deadZone').fill('0.65');
    await page.locator('#saveTilt').click();
    await expect(page.locator('#alert')).toContainText('Dead zone');
    expect(fs.readFileSync(file, 'utf8')).toBe(before);

    await page.locator('#deadZone').fill('0.14');
    const saved = page.waitForResponse((response) =>
      response.url().endsWith('/api/tilt') && response.request().method() === 'POST');
    await page.locator('#saveTilt').click();
    expect((await saved).status()).toBe(200);
    const persisted = JSON.parse(fs.readFileSync(file, 'utf8'));
    expect(persisted.AccelMapping.MaxTiltDegrees).toBe(35);
    expect(persisted.AccelMapping.Sensitivity).toBeCloseTo(1.7);
    expect(persisted.AccelMapping.DeadZone).toBeCloseTo(0.14);
    expect((await request.post('/api/profiles/reload', { headers: AUTH })).status()).toBe(200);
    await expect(page.locator('#maxTilt')).toHaveValue('35');
  });

  test('direct tilt API rejects invalid tuning without mutating the active profile', async ({ request }) => {
    writeProfile('Tilt API Validation');
    expect((await request.post('/api/profile', {
      headers: AUTH, data: { name: 'Tilt API Validation' }
    })).status()).toBe(200);
    const file = path.join(PROFILE_DIRECTORY, 'Tilt API Validation.json');
    const original = fs.readFileSync(file, 'utf8');
    const baseline = await (await request.get('/api/state', { headers: AUTH })).json();
    const valid = {
      ...baseline.tilt, enabled: true, maxTiltDegrees: 35, sensitivity: 1.6, deadZone: 0.2
    };

    for (const [field, value, expected] of [
      ['maxTiltDegrees', 95, 'Max tilt'],
      ['sensitivity', 5, 'Sensitivity'],
      ['deadZone', 0.6, 'Dead zone']
    ] as const) {
      const invalid = await request.post('/api/tilt', {
        headers: AUTH, data: { ...valid, [field]: value }
      });
      expect(invalid.status()).toBe(400);
      expect((await invalid.json()).error).toContain(expected);
      expect(fs.readFileSync(file, 'utf8')).toBe(original);
    }

    const saved = await request.post('/api/tilt', { headers: AUTH, data: valid });
    expect(saved.status()).toBe(200);
    const profile = JSON.parse(fs.readFileSync(file, 'utf8'));
    expect(profile.AccelMapping.MaxTiltDegrees).toBe(35);
    expect(profile.AccelMapping.Sensitivity).toBeCloseTo(1.6);
    expect(profile.AccelMapping.DeadZone).toBeCloseTo(0.2);
    expect(profile.AccelMapping.TiltSteeringEnabled).toBe(true);
    expect((await request.post('/api/profiles/reload', { headers: AUTH })).status()).toBe(200);
    const after = await (await request.get('/api/state', { headers: AUTH })).json();
    expect(after.tilt.maxTiltDegrees).toBe(35);
    expect(after.tilt.sensitivity).toBeCloseTo(1.6);

    // Older API callers may omit the numeric tuning fields, yielding zero defaults.
    const legacy = await request.post('/api/tilt', {
      headers: AUTH, data: { ...valid, maxTiltDegrees: 0, sensitivity: 0 }
    });
    expect(legacy.status()).toBe(200);
    const legacyProfile = JSON.parse(fs.readFileSync(file, 'utf8'));
    expect(legacyProfile.AccelMapping.MaxTiltDegrees).toBe(25);
    expect(legacyProfile.AccelMapping.Sensitivity).toBe(1);
    expect(legacyProfile.AccelMapping.DeadZone).toBeCloseTo(0.2);
  });

  test('tilt API rejects unknown target and axis without changing the profile', async ({ request }) => {
    writeProfile('Tilt Selector Validation');
    expect((await request.post('/api/profile', {
      headers: AUTH, data: { name: 'Tilt Selector Validation' }
    })).status()).toBe(200);
    const file = path.join(PROFILE_DIRECTORY, 'Tilt Selector Validation.json');
    const before = fs.readFileSync(file, 'utf8');
    const state = await (await request.get('/api/state', { headers: AUTH })).json();
    const valid = { ...state.tilt, enabled: true, maxTiltDegrees: 30, sensitivity: 1.4, deadZone: 0.1 };

    for (const [field, value, description] of [
      ['target', 'UnknownVirtualAxis', 'target'],
      ['axis', 'Q', 'axis']
    ] as const) {
      const rejected = await request.post('/api/tilt', {
        headers: AUTH, data: { ...valid, [field]: value }
      });
      expect(rejected.status()).toBe(400);
      expect((await rejected.json()).error.toLowerCase()).toContain(description);
      expect(fs.readFileSync(file, 'utf8')).toBe(before);
      const unchanged = await (await request.get('/api/state', { headers: AUTH })).json();
      expect(unchanged.tilt).toEqual(state.tilt);
    }

    const saved = await request.post('/api/tilt', {
      headers: AUTH, data: { ...valid, target: ' rightstickx ', axis: 'z' }
    });
    expect(saved.status()).toBe(200);
    const profile = JSON.parse(fs.readFileSync(file, 'utf8'));
    expect(profile.AccelMapping.TargetControl).toBe('RightStickX');
    expect(profile.AccelMapping.TiltAxis).toBe('Z');
    expect((await request.post('/api/profiles/reload', { headers: AUTH })).status()).toBe(200);

    // Preserve existing defaults for callers that omit the optional selectors.
    const legacy = await request.post('/api/tilt', {
      headers: AUTH, data: { ...valid, target: '', axis: '' }
    });
    expect(legacy.status()).toBe(200);
    const legacyProfile = JSON.parse(fs.readFileSync(file, 'utf8'));
    expect(legacyProfile.AccelMapping.TargetControl).toBe('LeftStick (Steering + Pitch)');
    expect(legacyProfile.AccelMapping.TiltAxis).toBe('X');
  });

  test('tilt editor explains planned gyro support and preserves existing gyro settings', async ({ page, request }) => {
    writeProfile('UI Gyro Enabled');
    expect((await request.post('/api/profile', {
      headers: AUTH, data: { name: 'UI Gyro Enabled' }
    })).status()).toBe(200);
    const before = await (await request.get('/api/state', { headers: AUTH })).json();
    expect((await request.post('/api/tilt', {
      headers: AUTH, data: { ...before.tilt, gyroEnabled: true }
    })).status()).toBe(200);

    await openProfiles(page);
    await expect(page.getByText('MotionPlus gyro mapping is planned', { exact: false })).toBeVisible();
    await expect(page.locator('#gyroEnabled')).toHaveCount(0);
    const save = page.waitForResponse((response) =>
      response.url().endsWith('/api/tilt') && response.request().method() === 'POST');
    await page.locator('#saveTilt').click();
    expect((await save).status()).toBe(200);
    const file = path.join(PROFILE_DIRECTORY, 'UI Gyro Enabled.json');
    expect(JSON.parse(fs.readFileSync(file, 'utf8')).AccelMapping.GyroEnabled).toBe(true);
  });

  test('direct tilt API persists explicit gyro enabled edits and preserves omitted gyro flag', async ({ request }) => {
    writeProfile('Gyro Enabled Mapping');
    expect((await request.post('/api/profile', {
      headers: AUTH, data: { name: 'Gyro Enabled Mapping' }
    })).status()).toBe(200);
    const file = path.join(PROFILE_DIRECTORY, 'Gyro Enabled Mapping.json');
    const before = await (await request.get('/api/state', { headers: AUTH })).json();
    expect(before.tilt.gyroEnabled).toBe(false);

    const enable = await request.post('/api/tilt', {
      headers: AUTH, data: { ...before.tilt, gyroEnabled: true }
    });
    expect(enable.status()).toBe(200);
    expect(JSON.parse(fs.readFileSync(file, 'utf8')).AccelMapping.GyroEnabled).toBe(true);
    expect((await (await request.get('/api/state', { headers: AUTH })).json()).tilt.gyroEnabled).toBe(true);

    // The current headless UI submits tilt controls only. It must not disable gyro.
    const tiltOnly = await request.post('/api/tilt', {
      headers: AUTH, data: {
        enabled: true, target: 'LeftStickX', axis: 'X',
        maxTiltDegrees: 35, sensitivity: 1.2, deadZone: 0.1,
        invertSteering: false, invertPitch: false, invertAxis: false
      }
    });
    expect(tiltOnly.status()).toBe(200);
    const unchanged = JSON.parse(fs.readFileSync(file, 'utf8')).AccelMapping;
    expect(unchanged.GyroEnabled).toBe(true);
    expect(unchanged.MaxTiltDegrees).toBe(35);

    const disable = await request.post('/api/tilt', {
      headers: AUTH, data: { ...before.tilt, gyroEnabled: false }
    });
    expect(disable.status()).toBe(200);
    expect(JSON.parse(fs.readFileSync(file, 'utf8')).AccelMapping.GyroEnabled).toBe(false);
    expect((await request.post('/api/profiles/reload', { headers: AUTH })).status()).toBe(200);
    expect((await (await request.get('/api/state', { headers: AUTH })).json()).tilt.gyroEnabled).toBe(false);
  });

  test('tilt save failure preserves the active in-memory profile and disk state', async ({ request }) => {
    writeProfile('Tilt Save Failure');
    expect((await request.post('/api/profile', {
      headers: AUTH, data: { name: 'Tilt Save Failure' }
    })).status()).toBe(200);
    const file = path.join(PROFILE_DIRECTORY, 'Tilt Save Failure.json');
    const original = fs.readFileSync(file, 'utf8');
    const before = await (await request.get('/api/state', { headers: AUTH })).json();
    // Make the private profile directory read-only to exercise the real save failure.
    const directoryMode = fs.statSync(PROFILE_DIRECTORY).mode & 0o777;
    fs.chmodSync(PROFILE_DIRECTORY, 0o555);
    try {
      const rejected = await request.post('/api/tilt', {
        headers: AUTH,
        data: {
          ...before.tilt,
          enabled: !before.tilt.enabled,
          maxTiltDegrees: 45,
          sensitivity: 2,
          deadZone: 0.3
        }
      });
      expect(rejected.status()).toBe(500);
      expect((await rejected.json()).error).toContain('Could not save profile');
      expect(fs.readFileSync(file, 'utf8')).toBe(original);
      const after = await (await request.get('/api/state', { headers: AUTH })).json();
      expect(after.tilt).toEqual(before.tilt);
    } finally {
      fs.chmodSync(PROFILE_DIRECTORY, directoryMode);
    }
  });

  test('edits Nunchuk and Classic buttons without overwriting either column', async ({ page, request }) => {
    writeProfile('Extension Buttons');
    const chosen = await request.post('/api/profile', {
      headers: AUTH, data: { name: 'Extension Buttons' }
    });
    expect(chosen.status()).toBe(200);
    await openProfiles(page);

    const row = page.locator('#mappings .map-row[data-target="A"]');
    const nunchuk = row.locator('select[data-source="nunchuk"]');
    const classic = row.locator('select[data-source="classic"]');
    await expect(nunchuk).toBeVisible();
    await expect(classic).toBeVisible();

    const first = page.waitForResponse((response) =>
      response.url().endsWith('/api/mapping/extension') && response.request().method() === 'POST');
    await nunchuk.selectOption('Z');
    expect((await first).status()).toBe(200);

    const second = page.waitForResponse((response) =>
      response.url().endsWith('/api/mapping/extension') && response.request().method() === 'POST');
    await classic.selectOption('ZR');
    expect((await second).status()).toBe(200);

    const profile = JSON.parse(fs.readFileSync(path.join(PROFILE_DIRECTORY, 'Extension Buttons.json'), 'utf8'));
    expect(profile.A.NunchukButton).toBe(2);
    expect(profile.A.ClassicButton).toBe(8);
    const reload = await request.post('/api/profiles/reload', { headers: AUTH });
    expect(reload.status()).toBe(200);
    await expect(nunchuk).toHaveValue('Z');
    await expect(classic).toHaveValue('ZR');
  });

  test('edits and persists extension stick targets and tuning', async ({ page, request }) => {
    writeProfile('Extension Sticks');
    const chosen = await request.post('/api/profile', {
      headers: AUTH, data: { name: 'Extension Sticks' }
    });
    expect(chosen.status()).toBe(200);
    await openProfiles(page);
    const editor = page.locator('#extensionStickEditor');
    await expect(editor).toBeVisible();

    const saved = page.waitForResponse((response) =>
      response.url().endsWith('/api/mapping/stick') && response.request().method() === 'POST');
    await editor.locator('select[data-source="Nunchuk"]').selectOption('RightStick');
    expect((await saved).status()).toBe(200);

    await editor.locator('#stickDeadZone').fill('0.25');
    await editor.locator('#stickSensitivity').fill('1.5');
    await editor.locator('#stickInvertY').check();
    const tuned = page.waitForResponse((response) =>
      response.url().endsWith('/api/mapping/stick') && response.request().method() === 'POST');
    await editor.getByRole('button', { name: 'Save stick tuning' }).click();
    expect((await tuned).status()).toBe(200);

    const profile = JSON.parse(fs.readFileSync(path.join(PROFILE_DIRECTORY, 'Extension Sticks.json'), 'utf8'));
    expect(profile.ExtensionStick.NunchukTarget).toBe('RightStick');
    expect(profile.ExtensionStick.DeadZone).toBeCloseTo(0.25);
    expect(profile.ExtensionStick.Sensitivity).toBeCloseTo(1.5);
    expect(profile.ExtensionStick.InvertY).toBe(true);
    const reloaded = await request.post('/api/profiles/reload', { headers: AUTH });
    expect(reloaded.status()).toBe(200);
    await expect(editor.locator('select[data-source="Nunchuk"]')).toHaveValue('RightStick');
    await expect(editor.locator('#stickDeadZone')).toHaveValue('0.25');
    await expect(editor.locator('#stickSensitivity')).toHaveValue('1.5');
    await expect(editor.locator('#stickInvertY')).toBeChecked();
  });

  test('persists both Classic stick targets and rejects invalid stick tuning', async ({ page, request }) => {
    writeProfile('Classic Sticks');
    expect((await request.post('/api/profile', {
      headers: AUTH, data: { name: 'Classic Sticks' }
    })).status()).toBe(200);
    await openProfiles(page);
    const editor = page.locator('#extensionStickEditor');
    const left = editor.locator('select[data-source="ClassicLeft"]');
    const right = editor.locator('select[data-source="ClassicRight"]');

    const first = page.waitForResponse((response) =>
      response.url().endsWith('/api/mapping/stick') && response.request().method() === 'POST');
    await left.selectOption('RightStick');
    expect((await first).status()).toBe(200);
    const second = page.waitForResponse((response) =>
      response.url().endsWith('/api/mapping/stick') && response.request().method() === 'POST');
    await right.selectOption('Unmapped');
    expect((await second).status()).toBe(200);

    const file = path.join(PROFILE_DIRECTORY, 'Classic Sticks.json');
    let profile = JSON.parse(fs.readFileSync(file, 'utf8'));
    expect(profile.ExtensionStick.ClassicLeftTarget).toBe('RightStick');
    expect(profile.ExtensionStick.ClassicRightTarget).toBe('Unmapped');
    expect(profile.ExtensionStick.NunchukTarget).toBe('LeftStick');

    await editor.locator('#stickDeadZone').fill('0.99');
    await editor.getByRole('button', { name: 'Save stick tuning' }).click();
    await expect(page.locator('#alert')).toContainText('Stick dead zone must be');
    profile = JSON.parse(fs.readFileSync(file, 'utf8'));
    expect(profile.ExtensionStick.DeadZone).toBeCloseTo(0.15);

    expect((await request.post('/api/profiles/reload', { headers: AUTH })).status()).toBe(200);
    await expect(left).toHaveValue('RightStick');
    await expect(right).toHaveValue('Unmapped');
  });

  test('extension mapping rejects invalid input without changing the saved profile', async ({ request }) => {
    writeProfile('Extension Validation');
    expect((await request.post('/api/profile', {
      headers: AUTH, data: { name: 'Extension Validation' }
    })).status()).toBe(200);
    const file = path.join(PROFILE_DIRECTORY, 'Extension Validation.json');
    const before = fs.readFileSync(file, 'utf8');
    const invalid = await request.post('/api/mapping/extension', {
      headers: AUTH, data: { target: 'A', nunchukButton: 'Invalid', classicButton: 'ZR' }
    });
    expect(invalid.status()).toBe(400);
    expect(fs.readFileSync(file, 'utf8')).toBe(before);
    const wrongStick = await request.post('/api/mapping/stick', {
      headers: AUTH, data: { source: 'Invalid', target: 'LeftStick' }
    });
    expect(wrongStick.status()).toBe(400);
    expect(fs.readFileSync(file, 'utf8')).toBe(before);
    const malformedStickAlias = await request.post('/api/mapping/stick', {
      headers: AUTH, data: { source: 'ClassicLe', target: 'LeftStick' }
    });
    expect(malformedStickAlias.status()).toBe(400);
    expect(fs.readFileSync(file, 'utf8')).toBe(before);
    const unauth = await request.post('/api/mapping/extension', {
      data: { target: 'A', nunchukButton: 'C', classicButton: 'ZR' }
    });
    expect(unauth.status()).toBe(401);
    expect(fs.readFileSync(file, 'utf8')).toBe(before);
  });

  test('rejects out-of-range extension stick tuning at the API without mutating the profile', async ({ request }) => {
    writeProfile('Extension Stick API Validation');
    expect((await request.post('/api/profile', {
      headers: AUTH, data: { name: 'Extension Stick API Validation' }
    })).status()).toBe(200);

    const file = path.join(PROFILE_DIRECTORY, 'Extension Stick API Validation.json');
    const before = fs.readFileSync(file, 'utf8');
    const invalidRequests = [
      { source: 'Nunchuk', target: 'RightStick', deadZone: -0.01 },
      { source: 'Nunchuk', target: 'RightStick', deadZone: 0.96 },
      { source: 'ClassicLeft', target: 'RightStick', sensitivity: 0.05 },
      { source: 'ClassicRight', target: 'LeftStick', sensitivity: 3.01 }
    ];

    for (const data of invalidRequests) {
      const response = await request.post('/api/mapping/stick', { headers: AUTH, data });
      expect(response.status(), JSON.stringify(data)).toBe(400);
      expect(fs.readFileSync(file, 'utf8')).toBe(before);
    }
  });

  test('extension stick API commits its target and tuning in one update', async ({ request }) => {
    writeProfile('Extension Stick Atomic Update');
    expect((await request.post('/api/profile', {
      headers: AUTH, data: { name: 'Extension Stick Atomic Update' }
    })).status()).toBe(200);

    const saved = await request.post('/api/mapping/stick', {
      headers: AUTH,
      data: {
        source: 'ClassicLeft', target: 'RightStick',
        deadZone: 0.35, sensitivity: 1.7, invertY: true
      }
    });
    expect(saved.status()).toBe(200);

    const file = path.join(PROFILE_DIRECTORY, 'Extension Stick Atomic Update.json');
    const profile = JSON.parse(fs.readFileSync(file, 'utf8'));
    expect(profile.ExtensionStick.ClassicLeftTarget).toBe('RightStick');
    expect(profile.ExtensionStick.DeadZone).toBeCloseTo(0.35);
    expect(profile.ExtensionStick.Sensitivity).toBeCloseTo(1.7);
    expect(profile.ExtensionStick.InvertY).toBe(true);
    expect((await request.post('/api/profiles/reload', { headers: AUTH })).status()).toBe(200);
    const after = await (await request.get('/api/state', { headers: AUTH })).json();
    expect(after.extensionStick).toMatchObject({
      classicLeftTarget: 'RightStick', deadZone: 0.35, sensitivity: 1.7, invertY: true
    });
  });

  test('extension stick save failure preserves the active profile and disk state', async ({ request }) => {
    writeProfile('Extension Stick Save Failure');
    expect((await request.post('/api/profile', {
      headers: AUTH, data: { name: 'Extension Stick Save Failure' }
    })).status()).toBe(200);

    const file = path.join(PROFILE_DIRECTORY, 'Extension Stick Save Failure.json');
    const original = fs.readFileSync(file, 'utf8');
    const before = await (await request.get('/api/state', { headers: AUTH })).json();
    // Make the private profile directory read-only to exercise the real save failure.
    const directoryMode = fs.statSync(PROFILE_DIRECTORY).mode & 0o777;
    fs.chmodSync(PROFILE_DIRECTORY, 0o555);
    try {
      const rejected = await request.post('/api/mapping/stick', {
        headers: AUTH,
        data: {
          source: 'Nunchuk', target: 'RightStick',
          deadZone: 0.25, sensitivity: 1.5, invertY: true
        }
      });
      expect(rejected.status()).toBe(500);
      expect((await rejected.json()).error).toContain('Could not save profile');
      expect(fs.readFileSync(file, 'utf8')).toBe(original);
      const after = await (await request.get('/api/state', { headers: AUTH })).json();
      expect(after.extensionStick).toEqual(before.extensionStick);
    } finally {
      fs.chmodSync(PROFILE_DIRECTORY, directoryMode);
    }
  });

  test('compares a profile with the active one', async ({ page }) => {
    writeProfile('Compare Me');
    writeProfile('Compare Me Too');

    await openProfiles(page);
    const card = page.locator('.profile-card', { hasText: 'Compare Me' }).first();
    await expect(card).toBeVisible();

    await card.getByRole('button', { name: 'Compare' }).click();

    const comparison = page.locator('#profileComparison');
    await expect(comparison).toBeVisible();
    await expect(comparison).toContainText('Compare: Compare Me');
    await expect(comparison.locator('.profile-comparison-row.head')).toBeVisible();

    // Opening another comparison replaces the first one instead of stacking them.
    await page.locator('.profile-card', { hasText: 'Compare Me Too' })
      .getByRole('button', { name: 'Compare' }).click();
    await expect(page.locator('#profileComparison')).toHaveCount(1);
    await expect(comparison).toContainText('Compare: Compare Me Too');

    await comparison.getByRole('button', { name: 'Close' }).click();
    await expect(page.locator('#profileComparison')).toHaveCount(0);
  });
});
