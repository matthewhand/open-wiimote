# OpenWiimote Roadmap

Ideas that are worth implementing, ranked by how much they improve the bridge between a
Nintendo Wii Remote and a virtual game controller. An item carries *done* when it works in
the tree and is covered by tests wherever the behaviour can be tested without a remote;
where only part of an item is built, the entry says which part is missing. Nothing here is
in flight, and an item without a *done* marker is not built at all.

## 1. Tilt steering that actually works (done, still needs a run with a real remote)

The accelerometer values were decoded with the wrong scale, and the tilt to axis mapping
used the raw Z axis for the pitch, which is pinned to the gravity vector and therefore
always saturated. The rework:

- The accelerometer is decoded in g. The remote keeps the zero point and the value of
  one gravity of every axis in its EEPROM, 0x80 and 0x9A, and both are the high eight
  bits of the ten bit axis value. One gravity is therefore `(0x9A - 0x80) * 4 = 104`
  counts and the readings are `(raw - 512) / 104`. The Linux `hid-wiimote` driver reports
  the same counts, it only leaves them unscaled.
- Steering and pitch are derived from the tilt angles (`atan2` of the horizontal axis and
  the gravity axis) instead of the raw axis values, so the deflection is proportional to
  the actual tilt.
- The single axis modes read the same angles. The Z axis has no direction, it reports how
  far the remote is tilted away from the horizontal.
- The maximum tilt angle, the dead zone, the sensitivity and the three inversion flags
  (steering, pitch and the single axis modes) are part of the profile and can be edited in
  the desktop window and on the headless page.

## 2. Nunchuk and Classic Controller support (done)

- Decode the Nunchuk payload (stick, C/Z buttons, ten bit accelerometer) and the Classic
  Controller payload (two sticks, triggers, fifteen buttons) following the layouts of the
  `hid-wiimote` driver and Dolphin (done).
- Turn the extension encryption off with `0x55` to `0xA400F0` and `0x00` to `0xA400FB`, read
  the six byte identifier at `0xA400FA` and decode the extension with the type it names
  (done). The identifiers are the WiiBrew table, which now includes the four that a Motion
  Plus reports (item 10). `WiimoteConnection.SetExtensionType` is a leftover that forces a
  type by hand and neither front end calls it. A Guitar, a drum kit and a turntable have no
  entry in the table yet, so those arrive as an unknown extension.
- Switch the data reporting mode to `0x35` (buttons, accelerometer and extension bytes)
  while an extension is plugged in, and back to `0x31` when it is removed (done). A status
  report that says the extension is gone clears the detected type before any leftover
  extension bytes in that report are decoded, so unplugging a Classic Controller removes it
  from that remote instead of leaving it drawn on every connected remote. The headless page
  only draws an extension while `extensionConnected` is true, and it puts that remote and
  the extension in one card with a view each.
- Map the extension buttons and sticks onto the virtual controller (done). `ControlMapping`
  carries a Nunchuk and a Classic Controller button next to the Wiimote one, the profile
  maps every stick to the left or the right stick, and the mapping editor has a combo box
  for each source. The defaults map Z to LT, C to RB, and the Classic Controller to the
  usual Xbox layout.

## 3. Bluetooth data reports (done)

A remote that is paired over Bluetooth confirms the input reports with an `0xA1` byte in
front of the report id. The parser strips it before dispatching, so USB and Bluetooth
reports share one parser.

## 4. Infrared pointer (done, needs a sensor bar to show anything)

The IR bytes of the `0x33`, `0x36` and `0x37` reports are decoded into `WiimoteState.IrPoints`.
The IR camera is switched on with the set 1 and set 2 reports (`0x13`, `0x1a`), the two
sensitivity blocks and the mode register, and the data reporting mode follows the setting:
`0x33` without an extension, `0x37` with one. The toggle is "Switch on the IR camera" in
the app and the headless page, off by default because the camera costs battery. Turning the
points into a cursor still needs the sensor bar picking and the smoothing from WiiBrew.

## 5. Rumble and LED control (done)

`SetRumble` and `SetLedMask` are driven from the app and the headless page. Rumble is one
switch for every remote. Player LEDs are per remote: a newly connected remote takes the
lowest free slot from 1 to 4, so a second remote lights LED 2 and keeps it if the first
disconnects. The LED buttons on the headless page send `POST /api/leds` with that remote's
`deviceKey`. A request without `deviceKey` still writes the same mask to every remote.
On Linux the kernel owns the LEDs, and the write goes to the LED class devices named by
`packaging/60-openwiimote.rules`. Until that rule is installed the kernel leaves every
remote on player 1 and the log says the LEDs could not be switched.

## 6. Battery reporting to the virtual controller (done in the app and the headless service)

The decoded battery level is forwarded through `IVirtualGamepad.SetBattery`. The ViGEm and
uinput backends do not override it yet, so the current platforms accept the call without
publishing the level to the operating system.

## 7. Profiles per device (done)

Both front ends resolve a profile through `AppSettings.DeviceProfiles`, keyed by the device
key or the Bluetooth address of the remote, so a two remote setup keeps its own profile per
remote. The picker sits in the detail card of the selected remote in the desktop window and
on the headless page, and `POST /api/deviceprofile` does the same over HTTP. The choice is
part of the settings, so it survives a restart, and a connected remote picks it up on its
next report.

## 8. Live dashboard in headless mode (done)

The headless page now mirrors the desktop window: token login, connected remotes, device
details, the avatar, the virtual controller, profile selection, the profile of one remote,
the tilt editor, the player LEDs, rumble and the activity log. It polls the state every 180 ms (1.5 s while the tab is
hidden), so about five times a second; streaming it with server sent events would cut that
polling. The mapping dropdowns save through `POST /api/mapping` (item 9). With no `--urls`
and no `OPENWIIMOTE_URLS`, the service listens on `http://127.0.0.1:8080`. A `.env` file
next to the project can set `OPENWIIMOTE_*`; a variable already in the environment wins,
and `--urls` still wins over `OPENWIIMOTE_URLS`.

## 9. Mapping editor (desktop and headless extension controls)

The desktop window edits Wiimote, Nunchuk and Classic Controller button mappings,
extension-stick targets and tilt settings (covered by ProfileEditorTests).
The headless Profiles tab now edits Wiimote/Nunchuk/Classic button columns and Nunchuk,
Classic-left and Classic-right stick destinations. It also saves shared extension-stick
dead zone, sensitivity and inverted-Y settings. Existing POST /api/mapping,
POST /api/mapping/extension and POST /api/mapping/stick endpoints persist edits;
WebUiSmokeTests exercises the headless editor with synthetic requests.
The headless tilt form handles accelerometer settings; the MotionPlus gyro-to-gamepad
mapping and physical-controller validation remain separately tracked under issue #5.
No hardware acceptance is implied by browser tests.

## 10. MotionPlus gyro mapping (planned)

Original Wii Remotes support tilt using their accelerometer. MotionPlus is a separate
accessory, or built into a Wii Remote Plus, and adds a gyroscope. Gyro-to-gamepad mapping
is planned and is not a supported feature in the current release.

The current code contains an `ExperimentalMotionPlus` stub marked unavailable; it never
supplies gyro samples or polls the unverified register block. Extension pass-through
decoding and profile gyro fields remain for compatibility. The existing decoder is research
code and is not used as a live sample source.

Before offering gyro mapping:

- Verify activation, streamed gyro data and extension pass-through on actual hardware.
- Find a reliable, safe per-sample freshness signal for kernel-owned remotes on standard
  Linux kernels. Identical gyro readings must not time out, and stale readings must not
  remain active. The experimental kernel patch in draft PR #17 is not a standard-kernel fix.
- Add hardware acceptance for startup, disconnect, reconnect, stationary and steady-rate
  motion; investigate calibration and drift.
- Present gyro controls only once the feature works and its limitations are clear.

Tilt steering remains available without MotionPlus. Issue #5 and draft PR #17 retain the
investigation for future work.

## 11. Packet replay harness (done)

`--replay <file>` feeds a captured report per line through the parser and prints the
decoded state, and `tools/sample-reports.txt` is a small golden capture. `--replay -`
reads standard input, so a log can be piped in. Protocol changes are now verifiable
without the hardware in front of you.

The tests go one step further: a fake HID device hands the real watcher and the real
connection a remote that only exists in the test process, so connecting, the extension
handshake, the Motion Plus passthrough frames, the experimental gyro boundary, the stale timeout and a denied
hidraw node are covered without a remote (`FakeWiimoteWatcherTests`).

## 12. Packaging

Installers, autostart on login, a tray icon and a single file publish turn the project
from a tool that has to be started from a terminal into something that is installed and
forgotten. Only the udev rules (`packaging/60-openwiimote.rules`) are in the tree. They
cover hidraw, the player LED class devices, and `/dev/uinput`. The installer, the
autostart entry, the tray icon and the publish profile do not exist.
