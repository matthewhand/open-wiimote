# Testing the headless service

The headless service is the part of OpenWiimote that can be tested without a display: it
serves the web UI and a small JSON API over HTTP. This document covers how to run it, what
the API does, and what is and is not covered by automated tests.

## Running it

```bash
# A token is required; without OPENWIIMOTE_ACCESS_TOKEN one is generated for the run.
OPENWIIMOTE_ACCESS_TOKEN=pick-a-token \
  dotnet run --project src/OpenWiimote.App/OpenWiimote.App.csproj -c Release \
  -- --headless --urls http://127.0.0.1:8188
```

Then open `http://127.0.0.1:8188/?token=pick-a-token`. The token is stored in
`localStorage`, so the link only has to be used once.

Without a token, setting `OPENWIIMOTE_ALLOW_ANONYMOUS=1` opens every route to anyone who
can reach the port. That is a convenience for a trusted LAN, and the service says so in its
startup output.

With no `--urls` and no `OPENWIIMOTE_URLS`, the service listens on `http://127.0.0.1:8080`.
A `.env` file next to the project can set `OPENWIIMOTE_*`. A variable already in the
environment wins, and `--urls` still wins over `OPENWIIMOTE_URLS`. A bad pair address
(`POST /api/pair`) answers 400 before any Bluetooth command runs.

Profiles live in `%APPDATA%/OpenWiimote/Profiles` on Windows and
`$XDG_CONFIG_HOME/OpenWiimote/Profiles` (usually `~/.config/OpenWiimote/Profiles`) elsewhere.
Set `XDG_CONFIG_HOME` to a scratch directory to keep a test run away from the real profiles.

## API

Every route except `/api/health` and `/api/auth` needs `Authorization: Bearer <token>`,
unless the service was started with `OPENWIIMOTE_ALLOW_ANONYMOUS=1`.

| Route | Method | Body | Effect |
|-------|--------|------|--------|
| `/api/health` | GET | — | `{ "status": "ok", "authRequired": true }`, no token needed; the page reads `authRequired` to decide whether it has to ask for one |
| `/api/auth` | POST | `{ "token": "..." }` | `{ "authenticated": true }` or 401 |
| `/api/state` | GET | — | The whole snapshot: devices, settings, profiles, tilt, logs |
| `/api/profiles` | GET | — | Profile names |
| `/api/profiles` | POST | `{ "name"?, "copyOf"? }` | Creates a profile (a copy of `copyOf`, or the default layout) and makes it active; an empty body picks the next free "New profile" name |
| `/api/profiles/reload` | POST | — | Re-reads the profile folder |
| `/api/profile` | POST | `{ "name": "X" }` | Switches to profile X |
| `/api/profile` | POST | `{ "name": "X", "renameTo": "Y" }` | Renames X to Y, keeping its settings |
| `/api/profile` | POST | `{ "name": "X", "delete": true }` | Deletes X and falls back to Default |
| `/api/tilt` | POST | `{ "enabled", "target", "axis", "maxTiltDegrees", "sensitivity", "deadZone", "invertSteering", "invertPitch", "invertAxis" }` | Saves tilt settings into the active profile |
| `/api/deviceprofile` | POST | `{ "deviceKey": "...", "profile": "Racing" }` | Pins one remote to a profile; a missing profile answers 400, and an empty or `null` `profile` puts the remote back on the active profile |
| `/api/settings` | POST | settings object | Saves the tuning and auto-connect settings |
| `/api/emulation` | POST | `{ "enabled": true }` | Starts or stops the virtual controller |
| `/api/rumble` | POST | `{ "enabled": true }` | Toggles rumble |
| `/api/mapping` | POST | `{ "target": "A", "button": "Home" }` | Saves one Wiimote button on the active profile; a missing target answers 400 |
| `/api/leds` | POST | `{ "value": 32, "deviceKey": "02:00:00:00:00:02" }` | Sets that remote's player LEDs. Without `deviceKey`, the same mask is written to every connected remote |
| `/api/scan`, `/api/disconnect`, `/api/logs/clear` | POST | — | Scan, disconnect every remote, clear the log |
| `/api/paireddevices` (also `/api/bluetooth/paired` and `/api/bluetooth/paireddevices`) | GET | — | The Bluetooth devices the operating system has already paired, which is what the pairing panel lists |
| `/api/bluetooth/scan` (also `/api/scan/bluetooth`) | GET or POST | — | Discover pairable Bluetooth devices |
| `/api/pair` (also `/api/bluetooth/pair`) | POST | `{ "address": "00:1E:35:11:22:33" }` | Pair and connect one address. An address that is not `AA:BB:CC:DD:EE:FF` answers 400. A Bluetooth failure answers 500 with the reason |

Renaming onto an existing name and deleting a missing profile answer 400 with an `error`
message; both leave the profile folder unchanged.

## Automated tests

```bash
./scripts/verify.sh         # build, every test suite, and the report replay
```

Individually: `dotnet test` (unit and window tests), `npx playwright test` (browser tests
against a real headless service).

`playwright.config.ts` starts the headless service on port 8199 with a throwaway
`XDG_CONFIG_HOME`, so the UI tests create and delete profiles without touching the real
profile folder.

- `WebUiSmokeTests.spec.ts` drives the real page: token login, a rejected token, the API
  staying behind the token, saving a Wiimote button from the mapping dropdown, and the
  compare/rename/delete buttons on the profile cards, checking the profile files on disk.
- `DeviceSearchTests.spec.ts` stubs `/api/state` with two remotes to exercise the device
  filter, including that a filter survives the next poll, and the per-remote profile picker:
  what it shows for a pinned remote and what it posts when one is picked or cleared.
- `DashboardExtrasTests.spec.ts` stubs `/api/state` and `/api/settings` to exercise the
  view switches, the tilt calibration and the extension panel without owning the hardware.

Every spec fails on an uncaught page error: the shared `test` in
`tests/OpenWiimote.App/support/fixtures.ts` collects `pageerror` events and asserts the list
is empty when the test ends, so a broken render cannot pass as a green test.

Playwright needs its browser once per machine: `npx playwright install chromium`.

## Replaying a capture

Protocol work can be checked without a remote by feeding recorded reports through the
parser:

```bash
dotnet run --project src/OpenWiimote.App/OpenWiimote.App.csproj -c Release \
  -- --replay tools/sample-reports.txt

# or pipe a log in
cat capture.txt | dotnet run --project src/OpenWiimote.App/OpenWiimote.App.csproj -c Release \
  -- --replay -
```

Each line holds one report as hex bytes separated by spaces (commas, colons, semicolons and
dashes work too, and bytes may carry a `0x` prefix); blank lines and lines starting with `#`
are skipped.
The output shows the decoded buttons, accelerometer, tilt, battery and extension flag per
line, and the command exits with 1 when a line cannot be read as hex. The
`ReplayTests` in `tests/OpenWiimote.Core.Tests` use the same helper against golden
packets.

## The fake remote

`tests/OpenWiimote.Core.Tests/FakeWiimoteDevice.cs` is a HID device that only exists in the
test process. `WiimoteWatcher` takes the source of the devices it enumerates, so a test hands
it a list that holds the fake one, and everything above it - the watcher, `WiimoteConnection`,
the report parser, the extension decoder and the view model of the desktop device list - is
the production code.

The fake provides synthetic acknowledgements and extension identifiers; the test queues
status and data reports and controls disappearance and permission failures. It is a model,
not evidence of a physical controller's behavior. `FakeWiimoteWatcherTests` covers
connecting, extension handshake, pass-through layouts, stale timeout and denied access.
It also verifies that experimental MotionPlus support issues no unverified gyro-register
reads. `MotionPlusGyroTests` exercises the retained legacy decoder only; that decoder is
not a live sample source. `HeadlessRecoveryTests` uses a synthetic virtual controller to
check profile isolation, replacement/disconnect handling, neutral release and output retry.

## What still needs a real remote

- Pairing the remote with the operating system, and the permissions of its hidraw node: the
  tests open a fake device. A node the user cannot read is covered through the fake, which is
  where the udev hint in the log comes from, but the rule itself is not.
- Reading reports from a Wiimote over hidraw or USB, including the reconnect behaviour when
  the remote sleeps.
- Tilt steering with the accelerometer: the maths is unit tested, the feel is not.
- The virtual controller: the Linux uinput and Windows ViGEm backends need a game to test
  against.
- The IR camera: the reports are decoded and replayed, but nothing in the tree emits infrared.

On Linux the remote shows up as `/dev/hidraw*` after `bluetoothctl connect <address>`.

A virtual device through `/dev/uhid` does not substitute for one: the watcher enumerates
through HidSharp, which only reports HID devices that hang off a USB device, and a uhid
device has no USB parent. Publishing a uhid device with Nintendo's ids on the Bluetooth
bus (or even the USB bus) produces a hidraw node with the right `HID_ID` in sysfs and the
right name, and the watcher still lists zero devices. Only the sysfs identity parsing
(`HidSysfs`) can be checked that way, and it is covered by unit tests.
