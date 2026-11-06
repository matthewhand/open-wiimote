<p align="center">
  <img src="docs/images/open-wiimote.svg" alt="OpenWiimote — bring your Wii Remote back into play" width="100%">
</p>

<p align="center"><strong>Give your Wii Remote a second life.</strong><br>
Use a powered sensor bar to point and click, or map buttons and tilt to a virtual gamepad.</p>

<p align="center">
  <a href="LICENSE">ISC licensed</a> · .NET 10 ·
  <a href="https://github.com/matthewhand/open-wiimote/releases/tag/v0.1.0-rc.11">Download rc.11</a> ·
  <a href="docs/Windows-Setup.md">Windows setup</a> ·
  <a href="docs/Linux-Setup.md">Linux setup</a>
</p>

## Point, click and play

OpenWiimote connects original Wii Remotes through your operating system's Bluetooth/HID
support. The native desktop app and authenticated browser dashboard share one core.

- **Sensor-bar mouse:** choose a remote and monitor, calibrate four targets, then switch
  **Mouse mode** on. A left-clicks, B right-clicks, and Home stops. A live IR inset helps
  you keep both lights visible during calibration. No MotionPlus is required.
- **Buttons and tilt:** map buttons, pitch and roll to an Xbox-style controller. Tune
  sensitivity, dead zones, inversion and tilt range, and save profiles per remote.
- **Live feedback:** inspect buttons, battery, acceleration and IR dots, animate the remote,
  and control player lights and rumble. Longer tools have named tabs; diagnostics collapse.
- **Recovery:** retain saved Bluetooth bonds and profile assignments, release active output
  on disconnect or lost tracking, and show backend failures clearly.
- **Offline development:** simulated remotes, report replay, window tests and browser tests
  let you work without hardware. GitHub Actions is off; verification runs locally.

An original remote provides accelerometer tilt and an IR camera. **Gyro/MotionPlus support
is experimental and unavailable.** The sensor bar must emit infrared light; an ordinary
RGB monitor light bar will not work.

## Download and run

Choose your package from [rc.11](https://github.com/matthewhand/open-wiimote/releases/tag/v0.1.0-rc.11).
Verify its companion checksum, then extract into a new folder. Keep the old version as rollback.

| Platform | Package and requirements | Current evidence |
| --- | --- | --- |
| Windows ARM64 | `win-arm64`; bundled runtime; mouse mode needs no driver | Real original Wiimote pairing, IR calibration, mouse movement and clicks confirmed on Windows 11 ARM with an ultrawide. |
| Windows Intel/AMD | `win-x64`; bundled runtime | Package and local checks prepared; executable tested under Windows 11 ARM x64 emulation. Native Intel/AMD hardware acceptance remains open. |
| Windows virtual gamepads | x64 app plus the separately installed, signed ViGEmBus driver; ARM64 driver on Windows ARM | Implemented; current driver is retired upstream. Two-controller/game acceptance remains open. |
| Linux x64 | `linux-x64`; .NET 10 runtime, Bluetooth/HID and uinput permissions | Local tests and earlier real-remote sessions; rc.11 two-remote and gameplay acceptance remains open. Pointer backend currently targets the whole X11 desktop. |
| macOS | Source only | Native HID/pairing needs verification; virtual-gamepad output is not implemented. |

**This is a prerelease.** Hardware coverage varies by platform; see
[platform status](docs/Platform-Support.md) for the remaining checks.

### Windows

Double-click **OpenWiimote.exe**. Use **Pair a Wiimote in Windows**; if the app's pairing
flow fails, **Open Windows pairing wizard** opens the built-in alternative. Use a no-code
option only if Windows offers it. Pairing behavior varies by Bluetooth driver and Windows version.

Open **Sensor bar pointer setup**, choose your remote and display, and click **Use selected
remote → Calibrate screen**. Aim at each green target, hold steady, and press/release the
large front **A** button when ready. Then enable **Mouse mode**. The cursor stays still
while calibrating; **Home** stops mouse output afterwards.

Mouse mode works without ViGEmBus. For virtual Xbox gamepads, read the separate driver
requirements in [Windows setup](docs/Windows-Setup.md).

### Linux

```sh
sha256sum -c openwiimote-0.1.0-rc.11-linux-x64.tar.gz.sha256
tar -xzf openwiimote-0.1.0-rc.11-linux-x64.tar.gz
cd openwiimote-0.1.0-rc.11-linux-x64
sha256sum -c SHA256SUMS
./check-linux-setup.sh
./openwiimote --headless --urls http://127.0.0.1:8080
```

Open **http://127.0.0.1:8080** and enter the token printed at startup. Pair with the OS,
choose a profile, then enable output. [Linux setup](docs/Linux-Setup.md) covers permissions;
[installation and rollback](docs/Linux-Release.md) covers service deployment.

## Build and verify locally

You need the **.NET 10 SDK**. Node, Python 3 and cached Playwright Chromium are needed for
all checks. Install development dependencies once with `npm ci` and
`npx playwright install chromium` when network access is available.

```sh
dotnet build
dotnet run --project src/OpenWiimote.App
# Browser dashboard:
dotnet run --project src/OpenWiimote.App -c Release -- --headless --urls http://127.0.0.1:8080
# Full local checks:
./scripts/verify.sh
```

On Windows, run `./scripts/verify.ps1`. The suite covers .NET core/window behavior,
JavaScript calibration math, browser/API recovery, report replay and dependency notices.
Strictly offline checks require dependencies and browsers to be cached beforehand.
Synthetic tests complement physical checks; they cannot prove Bluetooth or gameplay behavior.

The server defaults to localhost and requires a token. `OPENWIIMOTE_URLS` selects listen
addresses; `OPENWIIMOTE_ACCESS_TOKEN` supplies a persistent token. Keep settings and tokens
outside release folders and inspect local `.env` files before starting a development service.

## License and dependencies

OpenWiimote-owned code is [ISC](LICENSE). Keep the complete
[third-party notices](THIRD-PARTY-NOTICES.md) with distributions. Runtime, font and native
component licenses are included and checked during packaging; their own terms apply.
See [the license audit](docs/Licensing.md) and [release notes](docs/Release-Notes.md).

## Roadmap

- **Windows:** finish native Intel/AMD testing, two-remote handover, reconnect endurance,
  gamepad acceptance and gameplay; improve signing/distribution and the retired driver dependency.
- **Linux:** complete rc.11 two-remote/button/tilt/reconnect acceptance; verify the IR pointer
  on real hardware, then add per-monitor and native Wayland support.
- **macOS:** verify native pairing/HID access, design and implement virtual output, and
  establish Intel/Apple Silicon signing, permissions and packaging.
- **Shared:** broaden extension hardware coverage, reconcile desktop/dashboard UI, and test
  range, edge accuracy, tracking loss and profile recovery across supported platforms.
- **MotionPlus:** future work; prove acquisition and per-sample freshness on real hardware
  before enabling gyro output.

See the [full roadmap](ROADMAP.md) and [native acceptance checklist](docs/Platform-Support.md).
