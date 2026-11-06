# Windows setup

Download the Windows prerelease that matches your use:

| Package | Pointer | Virtual Xbox 360 gamepads |
| --- | --- | --- |
| `win-x64` on Intel/AMD Windows | Implemented | Requires the signed ViGEmBus driver |
| `win-x64` on Windows 11 ARM, under x64 emulation | Implemented; verify on your PC | Requires the ARM64 ViGEmBus driver |
| `win-arm64` native | Implemented | Current managed client bundles x86/x64 native libraries; use the x64 package for gamepads |

The archive includes a pinned .NET runtime and dependency notices. Extract it into a
new versioned folder and double-click **OpenWiimote.exe**. Pointer mode uses Windows
SendInput and does not need a gamepad driver or administrator rights. It controls normal
windows at the app's privilege level; Windows protects elevated apps and the secure desktop.
Do not disable those protections.

Settings and per-remote display calibration live in `%APPDATA%\OpenWiimote`, outside the
package. Keep the previous runtime folder as rollback. Verify the archive's SHA-256
against its companion `.sha256` file before opening it. `SHA256SUMS.json` inventories
extracted files. This candidate is unsigned; it is for prerelease testing, not an installer
with a public Windows signing claim.

## Mouse mode

**Mouse mode: Off / On** in the main header toggles the calibrated IR mouse. If setup
is incomplete, switching it on opens **Sensor bar pointer setup** and keeps output off.
Choose a remote and display, use the selected remote, then calibrate all four targets.
During full-screen calibration, the live IR inset shows raw dots from the selected
remote. Aim at the green target and hold still; when ready, press and release the
large front A button. The cursor stays still until calibration is complete. Zero or
one dot means the bar is not fully visible; more than two means extra infrared lights.
Aim at the powered bar and switch Mouse mode on. A is left click, B is right click,
and Home stops the mouse. The toggle follows the controller state and starts off on
launch. The native ARM64 package supports mouse mode; the x64-build requirement applies
only to virtual Xbox gamepads.

## Desktop layout

**Connect** contains pairing and connection actions; **Settings** contains timing options.
The right panel opens on **Sensors**, with a live IR dot preview for the selected remote
and its tilt avatar. Switch on the IR camera to inspect the powered sensor bar; dots
expire when actual IR reports stop. This is a coordinate view, not camera video.
**Mapping**, **Tilt**, and **Remote** tabs contain the longer controller controls.
**Troubleshooting** at the bottom starts collapsed and contains platform, pairing and
backend details plus the activity log. The native desktop and browser dashboard remain
separate interfaces; this does not claim complete visual parity.

## Pair and test

Choose **Pair a Wiimote in Windows** in the app. Cancel any old Windows PIN dialog,
press and release the red **SYNC** button, then select your remote when it appears.
The window starts searching automatically and retries for up to 30 seconds. Choose
**Find Wiimotes** to restart a completed search. Choose **Pair selected remote** while its lights flash. The app supplies the special
six-byte PIN through the Windows Bluetooth API; there is no numeric code to guess.
After a saved pairing, wake it with a single button press. 1 + 2 enters guest discovery,
which is a different pairing mode and is not used by this saved-pairing flow.

Alternatively, choose **Open Windows pairing wizard** in the same window. This stops
OpenWiimote discovery before launching the built-in legacy `DevicePairingWizard.exe`
from the Windows system directory, without administrator rights. Complete or cancel
that wizard before searching in OpenWiimote again. If it offers **Skip** or pairing
without a code, use that option. Windows versions and Bluetooth drivers vary; this
button cannot guarantee that Windows will omit a PIN prompt. If a PIN is required
with no skip option, cancel and return to the app's Wiimote-specific flow.

Only recognised Wii Remote names can be selected. Existing bonds are retained; the helper
never removes devices, disables another service or changes adapter security. Discovery and
authentication run in an owned process with a 25-second deadline and cancellation. Windows
must confirm authentication and the HID service before the app reports pairing completed;
a real controller connection and data reports are checked separately. This uses built-in
Windows Bluetooth/HID support and needs no third-party input driver.

Protocol reference: [xwiimote's published Bluetooth protocol](https://github.com/xwiimote/xwiimote/blob/master/doc/PROTOCOL).
Microsoft documents [BluetoothAuthenticateDevice](https://learn.microsoft.com/en-us/windows/win32/api/bluetoothapis/nf-bluetoothapis-bluetoothauthenticatedevice)
and [BluetoothSetServiceState](https://learn.microsoft.com/en-us/windows/win32/api/bluetoothapis/nf-bluetoothapis-bluetoothsetservicestate).
This implementation uses those native interfaces; it does not include another project's
pairing implementation.

If the remotes are still being used by Ubuntu, finish that session before switching
hosts. A remote's pairing with a different host may need to be established again when
returning to Ubuntu.

For gamepad output, use only the official signed
[ViGEmBus release](https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0).
Its installer includes x64, x86 and ARM64 drivers and requires administrator approval.
The dependency is retired upstream; OpenWiimote does not silently install it. Pointer
mode remains usable when the gamepad backend is unavailable.

Follow [pointer calibration](Pointer-Setup.md), then verify both virtual controllers in
Windows controller settings (`joy.cpl`) and a real game. Test every button, pitch/roll,
neutral return, a held button during disconnect, reconnects, app restart, both player
lights and switching the active pointer remote. Successful synthetic tests or a cross-build
do not replace those checks. GitHub CI stays off.

## Local diagnostics

Run from PowerShell in the extracted folder:

```powershell
Start-Process -FilePath .\OpenWiimote.exe -ArgumentList '--diagnostics', "$env:TEMP\openwiimote-diagnostics.json" -Wait
Get-Content "$env:TEMP\openwiimote-diagnostics.json"
```

The report checks architecture, display enumeration and gamepad backend availability.
It omits Bluetooth addresses and credentials. `--check-pointer-output` also sends a
movement at the current cursor position without clicking. It does not prove real IR
tracking or controller operation. Keep diagnostics private.

Native desktop mode is the supported Windows entry point for this candidate. The
existing advanced Windows headless HttpListener mode may require an HTTP URL reservation;
do not run the whole app as administrator to work around it.

With games closed, `--check-gamepad-output` additionally creates two temporary virtual
controllers, checks independent A/B states through XInput, returns both to neutral and
removes them. It does not test Wii Remote input.
