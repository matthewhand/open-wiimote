# Platform support and native acceptance

Updated 6 November 2026. OpenWiimote began on Windows; Windows, Linux and macOS are the
project's desktop targets. The shared .NET/Avalonia application is not evidence that its
Bluetooth and virtual-controller path works on every OS.

| Target | Code | Current evidence | Release gate |
| --- | --- | --- | --- |
| Linux x64 | uinput gamepad; HID input; desktop and headless app | Public rc.2 package, authenticated startup and offline tests; partial real-remote observations | Two-remote physical acceptance and a game session |
| Windows | ViGEmBus client; platform selection; shared app | Implementation exists; current native hardware acceptance unrecorded | Pairing, output and failure/reconnect tests on native Windows |
| macOS | Shared HID/UI code; placeholder output provider | No verified native hardware session or virtual-gamepad backend | HID/pairing verification, then a working output design and native acceptance |

The Windows driver dependency is [retired upstream](https://docs.nefarius.at/projects/ViGEm/End-of-Life/).
Evaluate the existing installation path and a sustainable backend before promising a new
Windows release. Do not install unsigned drivers or weaken system protections to make a
smoke test pass.

The macOS provider currently always reports unavailable. Signing the existing app does not
implement that provider. A native design must establish the actual HID APIs, entitlements,
permissions, signing and deployment model it requires. Apple's [virtual HID entitlement](https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.developer.hid.virtual.device)
and [DriverKit entitlement guidance](https://developer.apple.com/documentation/driverkit/requesting-entitlements-for-driverkit-development)
are starting points, not proof that a gamepad implementation has been delivered.

## Common acceptance checklist

Record OS/build, CPU architecture, controller model and backend/driver version. Keep device
addresses, tokens, local paths and raw logs private; publish a sanitised summary.

1. Build and run the local .NET, Node math, browser/API and licensing suites. Test both
   interfaces starting without a connected remote and with the backend unavailable.
2. Pair an original remote using the OS-native path. Do not destroy saved Bluetooth bonds
   as an automatic recovery step. Verify the existing bond works after an app restart.
3. Connect two remotes. Confirm independent virtual controllers, player slots and profiles.
4. Hold/release every button, including the D-pad and simultaneous combinations. Check the
   actual OS controller reports; dashboard highlights alone do not prove gamepad output.
5. From a level, buttons-up pose, check positive/negative roll and pitch, diagonal tilt,
   neutral return and a stationary hold. Original remotes do not provide gyro yaw.
6. Disconnect one remote while a button or axis is active. Its output must release safely;
   the other remote must keep working. Reconnect physically and verify its profile returns.
7. Restart the application; verify settings, recovery and clean shutdown. Test a normal
   game session with both remotes and record limitations rather than inferring success.

## Development boundaries

- Use one checkout per configured development environment; no per-task worktree sprawl.
  Coordinate before switching branches in a shared checkout.
- Keep GitHub Actions off. Use `scripts/verify.sh` on Linux/macOS or `scripts/verify.ps1`
  on Windows. Existing caches are required for network-isolated runs.
- Pure calibration/pose math in source is independently testable. Dashboard wiring,
  IR pointer experiments and physical MotionPlus work remain separate review scopes.
- The installed rc.2 is a runtime package, independent of the checkout. Preserve rollback.
- Private development history must never be pushed to the public repository. Public updates
  are reviewed source snapshots parented only on the existing public history.

## Publication decision

The existing rc.2 may be presented as a **Linux x64 prerelease with partial hardware
acceptance**. It is not a completed Windows/Linux/macOS stable release. The next release
must identify its exact source, pass native acceptance for every platform it claims, retain
license/notices, and receive a fresh source/privacy review for newly included material.
