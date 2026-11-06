# Platform support and native acceptance

Updated 6 November 2026. OpenWiimote targets Windows, Linux and macOS. Current hardware
coverage varies; a shared .NET/Avalonia application does not prove every native backend.

| Target | Implementation | Current evidence | Remaining checks |
| --- | --- | --- | --- |
| Windows 11 ARM64 mouse | Native SendInput, per-remote/display calibration and Bluetooth wizard fallback | Real original Wiimote pairing, fresh IR, calibration and mouse operation confirmed with rc.11 | Edge/range accuracy, endurance, second-remote handover and reconnects |
| Windows x64 | Portable package; mouse and ViGEmBus gamepad backends | Package startup/display/output probe under ARM x64 emulation | Native Intel/AMD acceptance and two-gamepad/gameplay testing |
| Linux x64 | uinput gamepad, HID input, desktop/dashboard; X11 whole-desktop pointer | Local full-suite checks and earlier real-remote observations | rc.11 two-remote, tilt, reconnect, gameplay and IR acceptance |
| macOS | Shared HID/UI code; unavailable gamepad provider | No verified native hardware session | Native acquisition, output design, signing/permissions, Intel/Apple Silicon acceptance |

Native ARM64 Windows pointer mode needs no ViGEmBus driver. Virtual gamepads require
the x64 app plus the appropriate separately installed driver. ViGEmBus is
[retired upstream](https://docs.nefarius.at/projects/ViGEm/End-of-Life/).
Linux pointer output currently needs X11/uinput; per-monitor and native Wayland support
remain roadmap items. macOS output is not implemented. MotionPlus remains unavailable.

## Native acceptance checklist

Keep device addresses, credentials, local paths and raw logs private. Publish a sanitized
summary identifying the OS, architecture, controller model, build and backend used.

1. Run the local build, .NET, math, browser/API, replay and licensing checks.
2. Pair a real remote through native OS support; preserve existing bonds during recovery.
3. Verify all buttons, D-pad combinations, player lights, battery and rumble with both remotes.
4. Check pitch/roll, diagonals, neutral return, stationary holds and actual gamepad output.
5. For mouse mode, inspect two fresh IR dots, capture four targets and test all edges/centre,
   A/B press/release, Home, missing/extra lights and range. Accelerometer-only tilt is not gyro yaw.
6. Verify remote selection, independent profiles/controllers and safe second-remote handover.
7. Disconnect with buttons/axes held; output must release. Reconnect and restart the app,
   verify remembered settings/profile, and confirm mouse mode starts off.
8. Complete a normal game or desktop session; record failures and limitations explicitly.

## Release boundaries

Keep GitHub Actions off and validate locally. Retain ISC and complete dependency notices.
Packages identify their exact public source commit. Public history contains reviewed source
snapshots parented only on public history; private development ancestry is never pushed.
The desktop and browser UI are separate interfaces and do not yet have full visual parity.
