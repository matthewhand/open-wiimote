# OpenWiimote roadmap

Target: an original Wii Remote that is dependable on Windows, Linux and macOS.
This roadmap distinguishes implemented code from native hardware acceptance. The public
rc.2 download remains a Linux x64 prerelease; source changes do not update that archive.

## Implemented foundation

- Shared .NET 10 core, Avalonia desktop app and headless browser dashboard.
- HID report parsing, accelerometer tilt, battery/IR readings, player LEDs and rumble.
- Nunchuk/Classic Controller decoding and mapping; other named extensions are not all decoded.
- JSON profiles, per-remote assignment, stick/trigger/button mappings and tilt tuning.
- Linux uinput and Windows ViGEmBus virtual-controller implementations.
- Watcher/session recovery, controller neutralisation and visible backend failures.
- Fake-HID, window, browser/API, replay and license/notice checks.
- Versioned Linux packaging with checksums, source provenance and rollback instructions.
- Independently tested calibration/pose math helpers. They are not a released wizard.

Physical extension compatibility, Bluetooth behaviour and gameplay still require measured
acceptance. Battery forwarding to the OS is not implemented merely because the backend
accepts a battery setter.

## 1. Finish Linux acceptance

Complete the [native checklist](docs/Platform-Support.md) with both real remotes: every
button, diagonal and neutral tilt, independent profiles/controllers, physical Bluetooth
loss/reconnect, shutdown neutralisation and a normal game session. Preserve Bluetooth bonds.
Record feel, responsiveness and limits. A moving dashboard avatar is not proof of output.

## 2. Restore first-class Windows verification

Run the shared tests on native Windows, reconcile pairing guidance with observed behaviour,
and verify ViGEmBus output, two controllers, reconnects, clean release and a game session.
The driver is retired upstream; evaluate a sustainable backend/installation story. Test
missing-driver, denied-access and failed-output recovery without weakening system security.
Deliver a Windows package only after its own native checks and dependency-notice review.

## 3. Implement and verify macOS output

First establish real Wii Remote pairing/HID access and desktop/headless startup on a Mac.
The current gamepad provider is a placeholder. Select a native virtual-HID approach and
prove a controller is visible to the OS and a game. Document actual signing, entitlement,
permission, installer and removal requirements. Do not promise Xbox identity or force
feedback without testing. Record Intel/Apple Silicon coverage separately.

## 4. Integrate calibration and pose UI

Build on the tested five-hold helpers: neutral, pitch up/down and left/right edge up.
Preserve gravity, reject ambiguous/unstable/stale captures and isolate state per remote.
Gravity gives pitch/roll, not yaw; a twist about vertical supplies no extra accelerometer
reference. Keep browser visual setup separate from gameplay mappings unless explicitly
implementing and verifying both. Review the draft dashboard/pointer work in small scopes.

## 5. Measure IR and pointer behaviour

Verify sensor-bar acquisition, geometry, missing/extra points, tilt, range and recovery on
real hardware. Decide how optional pointer controls should behave. Treat image position as
aiming information, not proof of a measured rotation. No pointer claim until integrated
source, native output and physical acceptance agree.

## 6. Keep distribution honest

Native packages/installers, autostart and tray polish follow native verification. Retain
ISC and the complete dependency/font/native notice bundle. Run local checks; GitHub Actions
stays disabled. Public updates must retain their independent source history, with private
settings, paths, device identifiers and discussions excluded.

## 7. MotionPlus remains future work

The `ExperimentalMotionPlus` stub is unavailable and never supplies samples. Existing
profile fields and pass-through decoders preserve compatibility; they do not provide a
verified gyro source. Before enabling it: prove activation/streamed data and a safe freshness
signal on standard kernels, then test steady rates, stationary holds, disconnect/reconnect,
pass-through, drift and mapping on real hardware. Original-remote tilt needs no MotionPlus.
