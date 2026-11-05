<p align="center">
  <img src="docs/images/open-wiimote.svg" alt="OpenWiimote — bring your Wii Remote back into play" width="100%">
</p>

<p align="center">
  <strong>Turn an original Wii Remote into an Xbox-style virtual controller.</strong><br>
  Map buttons and accelerometer tilt, watch live readings, and keep a profile for each remote.
</p>

<p align="center">
  <a href="LICENSE"><img alt="License: ISC" src="https://img.shields.io/badge/license-ISC-6ee7ca"></a>
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-73a7ff">
  <a href="https://github.com/matthewhand/open-wiimote/releases/tag/v0.1.0-rc.2"><img alt="Linux x64 prerelease rc.2" src="https://img.shields.io/badge/Linux_x64-rc.2_prerelease-ffcb7d"></a>
</p>

<p align="center">
  <a href="https://github.com/matthewhand/open-wiimote/releases/tag/v0.1.0-rc.2">Download rc.2</a> ·
  <a href="docs/Linux-Setup.md">Linux setup</a> ·
  <a href="docs/Platform-Support.md">Platform status</a> ·
  <a href="#roadmap">Roadmap</a>
</p>

## One remote, two ways to play

OpenWiimote reads Wii Remotes paired with the operating system and maps their inputs to a
virtual gamepad. An **Avalonia desktop app** and a **browser dashboard** share the same core.
The headless service can run on a machine without a display.

- **Buttons and tilt:** map buttons, roll and pitch to an Xbox-style controller, with dead
  zones, sensitivity, maximum tilt and inversion settings.
- **Profiles that follow the remote:** save JSON profiles, switch layouts, and assign a
  particular remote its own profile.
- **Live feedback:** inspect buttons, acceleration, battery and IR points; use the avatar,
  accelerometer graph, player lights and rumble controls.
- **Extension inputs:** Nunchuk and Classic Controller decoding and mappings are implemented;
  physical compatibility still needs broader testing.
- **Reconnect handling:** retain Bluetooth bonds and profile assignments, release controller
  output on disconnect, and report backend failures in the dashboard.
- **Hardware-free development:** replay captured reports and test with simulated remotes.

An original Wii Remote already supports accelerometer tilt. **MotionPlus is not required
for tilt. Gyro mapping remains experimental and unavailable.**

## What is ready?

**rc.2 is a Linux x64 prerelease**, requiring the .NET 10 runtime. It is already published;
full two-remote acceptance and a normal game session remain incomplete. Windows and macOS
are project targets, not equally verified releases.

| Platform | Current implementation | Verification / next step |
| --- | --- | --- |
| **Linux** | uinput virtual gamepad; desktop and headless app | rc.2 packaged and installed. Tilt and one app-level reconnect observed; complete the two-remote checklist. |
| **Windows** | ViGEmBus-based Xbox 360 backend; shared app | Native build, Bluetooth pairing and real gamepad checks still required. Existing driver dependency is retired upstream. |
| **macOS** | Shared app source and an unavailable backend placeholder | HID/pairing needs native verification. Virtual-gamepad output is not implemented. |

Source after rc.2 includes tested calibration and pose **helpers**. The guided wizard and
IR/pointer integration remain roadmap work; they are not in the downloadable rc.2 binary.
See [platform support](docs/Platform-Support.md) and the [quality plan](docs/Quality-Plan.md)
for code, test and hardware evidence.

## Try the Linux prerelease

Download the archive and checksum from [the rc.2 release](https://github.com/matthewhand/open-wiimote/releases/tag/v0.1.0-rc.2).

```sh
sha256sum -c openwiimote-0.1.0-rc.2-linux-x64.tar.gz.sha256
tar -xzf openwiimote-0.1.0-rc.2-linux-x64.tar.gz
cd openwiimote-0.1.0-rc.2-linux-x64
sha256sum -c SHA256SUMS
./check-linux-setup.sh
./openwiimote --headless --urls http://127.0.0.1:8080
```

Open **http://127.0.0.1:8080**, then paste the access token printed at startup. Pair the
remote with the operating system, choose a profile, and enable virtual-controller output.
The setup check is read-only. [Linux setup](docs/Linux-Setup.md) explains Bluetooth,
`hidraw`/uinput permissions and recovery; running the application as root is not required.

Keep settings and credentials outside the release folder. For an existing installation,
follow the [upgrade and rollback guide](docs/Linux-Release.md) and keep the previous release.
The GitHub repository has been renamed; the existing rc.2 archive filenames remain unchanged.

## Build from source

Use the existing checkout. You need the **.NET 10 SDK**; Node and Playwright are needed
only for tests. The desktop UI uses Avalonia 11.3.

```sh
dotnet build
dotnet run --project src/OpenWiimote.App
```

For the browser dashboard:

```sh
dotnet run --project src/OpenWiimote.App -c Release -- --headless --urls http://127.0.0.1:8080
```

These `dotnet` commands also work in PowerShell. A token is generated and printed unless
you supply `OPENWIIMOTE_ACCESS_TOKEN`. The default is localhost; keep authentication enabled
when deliberately exposing the service to your LAN. A nearby `.env` can supply listen
settings, so inspect it before starting a development service.

| Setting | Purpose |
| --- | --- |
| `OPENWIIMOTE_ACCESS_TOKEN` | Use a persistent private token instead of a generated one. |
| `OPENWIIMOTE_URLS` | Set listen addresses; explicit `--urls` takes precedence. |
| `OPENWIIMOTE_ALLOW_ANONYMOUS=1` | Explicitly disables authentication. Leave unset for normal use. |

Windows virtual output needs the external ViGEmBus driver. It is [retired upstream](https://docs.nefarius.at/projects/ViGEm/End-of-Life/);
read the upstream guidance before setting up an existing installation. OpenWiimote does not
install drivers automatically. macOS can be developed without promising virtual output.

## Local checks

Install development dependencies once with `npm ci` and `npx playwright install chromium`.
Then run the same suites locally on each development platform:

```sh
# Linux / macOS
./scripts/verify.sh
```

```powershell
# Windows (or PowerShell 7 on another platform)
./scripts/verify.ps1
```

The check scripts run the .NET build/tests, JavaScript calibration/pose tests, browser/API
tests, report replay and licensing regressions. Python 3 is needed for the licensing checks.
Dependencies and Chromium must already be cached for strictly offline execution.
**GitHub Actions remains disabled; validation is local.**

Run the JavaScript suites individually with `npm test` and `npm run test:browser`.
Browser tests use a separate configuration folder and their own service instance. Passing
synthetic tests does not verify Bluetooth radio behaviour, driver installation or game feel.

## Project map

| Path | Contents |
| --- | --- |
| `src/OpenWiimote.Core` | HID protocol, mappings, profiles, settings and replay |
| `src/OpenWiimote.App` | Desktop app, headless HTTP service and dashboard |
| `src/OpenWiimote.Platform.*` | Platform-specific virtual-controller backends |
| `tests/` | .NET, JavaScript, browser/API and licensing checks |
| `tools/` | Bluetooth helper and sample report capture |
| `packaging/` | Linux udev rules and optional service example |

Settings, profiles and logs live under `%APPDATA%\OpenWiimote` on Windows, or the .NET
application-data location on other platforms. `XDG_CONFIG_HOME` overrides the base directory;
Linux normally uses `~/.config/OpenWiimote`. Each OS uses its own Bluetooth pairing and
controller permissions; a build on one OS does not prove hardware support on another.

## Documentation and license

[Setup](docs/Linux-Setup.md) · [Installation / rollback](docs/Linux-Release.md) ·
[Platform support](docs/Platform-Support.md) · [Quality and acceptance](docs/Quality-Plan.md) ·
[Hardware checklist](docs/Hardware-Bringup.md) · [API notes](docs/Integration-Test-Approach.md)

OpenWiimote-owned source is [ISC licensed](LICENSE), copyright 2026 Matthew Hand.
Dependencies retain their own licenses. Keep the [full third-party notices](THIRD-PARTY-NOTICES.md)
and `third_party/` bundle with redistributed binaries. The packager validates reviewed
versions and hashes; see [license provenance](docs/Licensing.md). Independent community
project; not affiliated with Nintendo.

## Roadmap

The next milestone is a dependable original-Wii-Remote experience on **Windows, Linux and macOS**.

- [ ] **Linux:** finish buttons, neutral pose, pitch/roll and physical Bluetooth reconnect
  checks with both remotes, then record a normal game session.
- [ ] **Windows:** verify native build and pairing, two independent virtual controllers,
  disconnect neutralisation and game compatibility; evaluate the retired driver dependency.
- [ ] **macOS / OS X:** verify HID access and pairing on a real Mac; select and implement a
  virtual-gamepad backend with documented signing, entitlement and installation needs.
- [ ] **Calibration:** integrate the tested five-hold pitch/roll helpers into the dashboard,
  with per-remote state, clear stillness guidance and real-controller acceptance.
- [ ] **IR and pointer controls:** review the experimental work and measure it with a sensor bar.
- [ ] **Distribution:** produce and validate platform-native packages after native acceptance.
- [ ] **MotionPlus:** revisit gyro activation, fresh sample acquisition and drift only after
  standard-kernel and real-hardware verification. It remains unavailable today.

See [ROADMAP.md](ROADMAP.md) for engineering detail and [platform support](docs/Platform-Support.md)
for the native verification checklist. These are open tasks, not claims of completed support.
