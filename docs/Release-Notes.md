# OpenWiimote 0.1.0-rc.11

A prerelease with Windows sensor-bar mouse control, portable x64/ARM64 packages and the
shared Linux desktop/dashboard source. MotionPlus remains experimental and unavailable.

## Included

- One selected remote owns the mouse, with calibration saved per remote/display.
- Four full-screen targets, live IR dot preview, tracking/steady feedback and explicit A guidance.
- Main-window Mouse mode toggle; A left-click, B right-click, Home stop.
- Stale/missing/extra IR handling, disconnect release, safe handover and startup with mouse off.
- Physical Windows monitor coordinates across mixed scaling and negative positions.
- Wiimote-aware native pairing plus a built-in Windows wizard fallback; saved bonds retained.
- Tabs and resizable desktop panes; platform details and Activity in collapsed Troubleshooting.
- Atomic settings/profile replacement fixes found by native Windows testing.
- Existing button/tilt mappings, per-remote profiles, Linux reconnect/player lights and browser avatar setup.
- Offline dependency/runtime verification, complete ISC/dependency notices and package checksums.

## Verification

314 .NET tests passed on Linux and native Windows ARM: 281 core and 33 app/window cases.
The full local suite also includes JavaScript math, browser/API recovery, report replay
and license-notice checks. Exact packaging inputs and source commit are in `release.json`.
Both Windows architectures include a pinned .NET runtime. Linux packages need .NET 10.

On Windows 11 ARM, a real original Wiimote paired using the built-in wizard. Live IR dots,
four-target calibration and mouse cursor control were exercised, and the user confirmed
mouse movement and clicks. This establishes one hardware session, not universal compatibility.
The x64 executable was exercised under ARM emulation; native Intel/AMD hardware acceptance
is still open. No ViGEmBus driver was installed in that session.

## Remaining acceptance and limitations

- Finish edge/range accuracy, click/Home details, tracking-loss and disconnect/reconnect endurance,
  second-remote handover, two independent virtual gamepads and a normal game session.
- Linux rc.11 is locally checked; real two-remote/tilt/gameplay and IR acceptance are incomplete.
  The pointer maps the whole X11 desktop; native Wayland and individual Linux monitors are future work.
- Native ARM64 mouse mode works without a gamepad driver. Virtual Xbox output requires the
  x64 app and the appropriate signed ViGEmBus driver, which is retired upstream.
- macOS has no implemented virtual-gamepad backend or verified native hardware session.
- Extension decoding/mapping is implemented, but physical compatibility needs broader testing.
- An original remote has no gyro. The unavailable MotionPlus stub never invents samples.

This release retains the earlier rc.2 download as rollback. Public source updates contain
reviewed snapshots and do not include private development history, settings or raw hardware logs.
GitHub Actions remains disabled; checks and packaging run locally.
