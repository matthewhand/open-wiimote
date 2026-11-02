/**
 * Builders for the stubbed /api/state snapshot the UI specs serve.
 *
 * The page only needs the fields it renders, so this keeps a complete snapshot in one
 * place instead of repeating it in every spec that needs a connected remote.
 */

export const TOKEN = 'playwright-token';
export const AUTH = { Authorization: `Bearer ${TOKEN}` };

export function device(
  key: string,
  name: string,
  bluetoothAddress: string,
  batteryPercent = 80,
  state: Record<string, unknown> = {}
) {
  return {
    key,
    name,
    bluetoothAddress,
    state: {
      batteryPercent,
      buttons: 0,
      accelX: 0,
      accelY: 0,
      accelZ: 1,
      lastReport: new Date().toISOString(),
      extensionConnected: false,
      extensionType: 0,
      tiltXDegrees: 0,
      tiltYDegrees: 0,
      ...state
    }
  };
}

export function stateWith(devices: unknown[]) {
  return {
    platform: 'Linux',
    bluetoothHint: '',
    gamepadBackend: 'Linux uinput',
    gamepadDescription: '',
    gamepadAvailable: true,
    gamepadError: null,
    emulationEnabled: false,
    emulationError: null,
    rumbleEnabled: false,
    settings: {
      autoConnect: true,
      pollIntervalMs: 1000,
      reconnectDelayMs: 3000,
      staleTimeoutMs: 6000,
      statusRequestIntervalMs: 1000,
      ledMask: 0,
      autoEnableEmulation: false,
      preferredProfile: 'Default',
      avatarTheme: 'wiimote',
      avatarRainEnabled: false,
      sparklineEnabled: false,
      keyboardShortcutsEnabled: false,
      rumbleShakeEnabled: false,
      batterySegmentsEnabled: false,
      deviceProfiles: {}
    },
    profiles: ['Default'],
    profileInfos: [
      {
        name: 'Default',
        description: 'Classic NES-style horizontal Wiimote mapping',
        tiltSteeringEnabled: false,
        tiltTarget: 'None',
        tiltAxis: 'X',
        iconEmoji: '',
        mappings: []
      }
    ],
    activeProfile: 'Default',
    profileDescription: '',
    profileDirectory: '/tmp',
    mappings: [],
    logs: [],
    tilt: {
      enabled: false,
      target: 'LeftStick (Steering + Pitch)',
      axis: 'X',
      maxTiltDegrees: 25,
      sensitivity: 1,
      deadZone: 0.05,
      invertSteering: false,
      invertPitch: false,
      invertAxis: false
    },
    devices
  };
}
