# OpenWiimote

A cross-platform bridge between a Nintendo Wii Remote and a virtual game controller. It
finds a remote the operating system has already paired, decodes its HID reports, maps its
buttons, sticks and tilt onto an Xbox 360 style layout, and publishes that through the
platform's virtual gamepad.

Two front ends share one core:

- an **Avalonia desktop window** with the device list, the mapping editor, the live avatar
  and the activity log;
- a **headless HTTP service** that serves a web dashboard, for a machine with no display.

## What it does today

- Reads a paired Wiimote over Bluetooth or USB and follows it through sleeps and reconnects.
- Decodes buttons, the ten bit accelerometer, tilt, battery and the IR camera, and shows
  them live: a 3D avatar that follows the remote, plus the IR point coordinates. The headless
  page adds an accelerometer sparkline and draws the IR points on a canvas.
- Nunchuk and Classic Controller buttons, sticks and triggers are decoded and can be mapped
  to any virtual control, including through a Motion Plus: the controller plugged into one
  arrives in the passthrough frames and is decoded as that controller. MotionPlus gyro
  mapping is planned. The `ExperimentalMotionPlus` stub is explicitly unavailable and
  supplies no samples; the current connection does not poll unverified gyro registers.
  Original Wii Remotes have tilt sensors but need a MotionPlus accessory for gyro. Other extensions (Guitar, drum kit, turntable)
  are reported by name when their identifier is known, but their payload is not decoded.
- Tilt steering: roll and pitch drive a stick or a trigger, with the maximum tilt, the dead
  zone, the sensitivity and the inversion flags stored per profile.
- Rumble and the four player LEDs. Each new remote takes the lowest free player slot, so
  the second remote lights LED 2. The LED buttons on the headless page change the selected
  remote. The battery level is forwarded to the virtual controller (the ViGEm and uinput
  backends accept the call without publishing it to the OS yet).
- Profiles are JSON files, edited in either front end, and one remote can be pinned to a
  profile of its own. The headless mapping dropdowns save through `POST /api/mapping`.
- Packet replay: a captured log of raw reports can be decoded without any hardware.

`ROADMAP.md` is the honest list of what is built and what is still open. The protocol path is
driven by a fake remote inside the tests; what still needs hardware of its own is listed in
`docs/Hardware-Bringup.md`, which walks through the bring-up of a real remote.

## Requirements

- .NET 10 SDK (every project targets `net10.0`).
- A virtual controller backend, or the app runs read-only without one:
  - **Linux**: `/dev/uinput`, from the `uinput` kernel module. No daemon.
  - **Windows**: the ViGEmBus driver, which supplies the Xbox 360 controller.
  - **macOS**: not implemented. A virtual controller needs Apple's restricted
    `com.apple.developer.hid.virtual.device` entitlement, so an unsigned build reports the
    backend as unavailable and leaves emulation off. Reading the remote, the LEDs and rumble
    work without it.
- Node 22 and Playwright, for the browser tests only.

## Build and run

```bash
dotnet build
dotnet run --project src/OpenWiimote.App
```

The remote has to be paired with the operating system first (Bluetooth settings, or
`bluetoothctl pair` on Linux); the app only opens HID devices the OS already knows about.

### Linux permissions

Start with `./scripts/check-linux-setup.sh`; it makes no changes. See
[Linux setup and recovery](docs/Linux-Setup.md) for permissions and a real-controller checklist.

Reading a remote and switching its LEDs needs access to `/dev/hidraw*` and
`/sys/class/leds/*`. The udev rules grant that to `plugdev`. The same file grants
`/dev/uinput` to group `input` and tags it for the active seat. `plugdev` alone does not
create the virtual gamepad.

```bash
sudo install -m 0644 packaging/60-openwiimote.rules /etc/udev/rules.d/
sudo udevadm control --reload-rules
sudo udevadm trigger --subsystem-match=hidraw
sudo udevadm trigger --subsystem-match=leds
sudo udevadm trigger --subsystem-match=misc --sysname-match=uinput
```

Pairing from the web dashboard can use `tools/openwiimote-helper.sh`, a wrapper around
`bluetoothctl`. Linux does not honor setuid on a shell script, so install it as a normal
executable or run the printed commands yourself. The dashboard only accepts an address of
the form `AA:BB:CC:DD:EE:FF`.

With no `--urls` and no `OPENWIIMOTE_URLS`, the headless service listens on
`http://127.0.0.1:8080`. A `.env` file next to the project can set
`OPENWIIMOTE_URLS`; a value already in the environment wins. The default release
listens on localhost unless you deliberately configure a LAN address.

## The headless service

```bash
OPENWIIMOTE_ACCESS_TOKEN=pick-a-token \
  dotnet run --project src/OpenWiimote.App -c Release -- --headless --urls http://127.0.0.1:8188
```

Then open `http://127.0.0.1:8188/?token=pick-a-token`. The token is kept in `localStorage`,
so the link is only needed once.

| Variable | Effect |
|----------|--------|
| `OPENWIIMOTE_ACCESS_TOKEN` | The token the page and the API ask for. Without it, one is generated for the run and printed at startup. |
| `OPENWIIMOTE_ALLOW_ANONYMOUS=1` | Opens every route to anyone who can reach the port. Only for a network you trust; the service says so in its startup output. |
| `OPENWIIMOTE_URLS` | Listen addresses, as `--urls` takes them. A wildcard host prints the addresses the dashboard is reachable on. |

The full route list is in `docs/Integration-Test-Approach.md`.

## Linux release candidates

Use `scripts/package-linux.sh VERSION OUTPUT_DIRECTORY` from clean committed sources
with cached dependencies. It creates a versioned private candidate, manifest and checksums
without installing it or running hosted CI. See [Linux release preparation](docs/Linux-Release.md)
for offline packaging, installation, rollback and public-distribution checks.
MotionPlus remains experimental; physical acceptance is still in progress.

## Replaying a capture

```bash
dotnet run --project src/OpenWiimote.App -- --replay tools/sample-reports.txt
cat capture.txt | dotnet run --project src/OpenWiimote.App -- --replay -
```

One report per line, as hex bytes. This is how the protocol is checked without a remote in
front of the machine.

## Where the files live

| What | Windows | Linux and macOS |
|------|---------|-----------------|
| Settings | `%APPDATA%\OpenWiimote\settings.json` | `$XDG_CONFIG_HOME/OpenWiimote/settings.json` (usually `~/.config/OpenWiimote`) |
| Profiles | `%APPDATA%\OpenWiimote\Profiles` | `$XDG_CONFIG_HOME/OpenWiimote/Profiles` |
| Logs | `%APPDATA%\OpenWiimote\Logs` | `$XDG_CONFIG_HOME/OpenWiimote/Logs` |

The desktop window writes `session_<timestamp>.log`, the headless service
`headless_<timestamp>.log`.

## Tests

```bash
./scripts/verify.sh    # build, every test suite, and the report replay
```

That is also what CI runs, so a green local run and a green pipeline mean the same thing.
Individually:

- `dotnet test` — the protocol, mapping, settings and profile tests, the watcher and
  connection tests that run against a fake HID device in the test process, and the window
  tests that drive the real Avalonia window on the headless platform.
- `npx playwright test` — the browser tests, against a real headless service with a throwaway
  config home. Needs `npx playwright install chromium` once per machine.

## Layout

| Path | What is in it |
|------|---------------|
| `src/OpenWiimote.Core` | HID protocol, report parser, auto-connect watcher, mapping, profiles, settings |
| `src/OpenWiimote.App` | Avalonia window, headless service, and `wwwroot` with the dashboard |
| `src/OpenWiimote.Platform.*` | The virtual gamepad of each platform |
| `tests/` | xUnit suites for the core and the window, Playwright specs for the page |
| `tools/` | The Bluetooth helper, a golden report capture, and a LAN viewer that reads the evdev nodes directly |
| `packaging/` | The udev rules |

## Documentation

| Document | Covers |
|----------|--------|
| `docs/Quality-Plan.md` | Quality work, offline evidence and remaining hardware acceptance |
| `docs/Linux-Setup.md` | Read-only diagnostics, permissions and recovery |
| `docs/Linux-Release.md` | Versioned packages, installation, rollback and publication checks |
| `docs/Release-Notes.md` | Candidate scope, compatibility and acceptance still pending |
| `ROADMAP.md` | What is built, what is half built, and what is not started |
| `docs/Hardware-Bringup.md` | The checklist for bringing up a real remote, with the log lines to expect |
| `docs/Integration-Test-Approach.md` | Running the headless service, its API, and what the automated tests do and do not cover |
| `docs/WINDOWS_SERVICE_ISSUES.md` | An unverified field note about Bluetooth service activation on Windows |

## License and dependencies

OpenWiimote-owned source is available under the [ISC license](LICENSE), copyright
2026 Matthew Hand. Dependencies keep their own licenses. The
[third-party notice bundle](THIRD-PARTY-NOTICES.md) includes full upstream texts for
runtime packages, embedded fonts/native components, the app host and development tooling.
The Linux packager validates the reviewed versions and notice hashes and includes these
files automatically. See [the licensing audit](docs/Licensing.md) for provenance and updates.

## rc.2 validation status

This release candidate passed 258 .NET and 51 browser tests before the notice-only changes;
nine licensing guards and the rc.2 package startup/authentication/shutdown checks passed.
Two-remotes physical Bluetooth reconnect, full buttons/pitch/neutral and a game session
remain unverified. MotionPlus is experimental and unavailable. GitHub Actions is disabled.
See PUBLIC-RELEASE-PROVENANCE.json for the reviewed snapshot and binary build provenance.
