# OpenWiimote roadmap

rc.11 adds calibrated Windows IR mouse control and portable x64/ARM64 packages. Native
Windows ARM hardware testing confirmed one original Wiimote mouse session. Linux checks
remain partial on physical hardware; macOS virtual output is not implemented.

1. **Windows:** native Intel/AMD coverage, edge/range and reconnect endurance, second-remote
   handover, two independent virtual gamepads and gameplay. Improve signing/distribution and
   find a sustainable path beyond the retired ViGEmBus dependency. Native ARM64 mouse mode
   remains independent of that driver.
2. **Linux:** finish both-remotes button/tilt/reconnect/game acceptance on rc.11; verify real
   IR acquisition/output, individual monitors and native Wayland support.
3. **macOS:** establish pairing/HID access, implement and verify virtual output, and document
   actual signing, entitlement, permissions and installer/removal requirements on Intel and Apple Silicon.
4. **Shared experience:** reconcile desktop/browser layout, improve onboarding and calibration,
   broaden Nunchuk/Classic Controller hardware coverage, and measure latency, range and recovery.
5. **Release quality:** retain versioned rollback packages, exact public-source provenance,
   source/privacy review, complete ISC/dependency notices and offline checks. GitHub CI stays off.
6. **MotionPlus:** future work. The experimental stub is unavailable. Before enabling gyro,
   prove activation, streamed data and reliable per-sample freshness on standard kernels,
   then test steady rates, stationary holds, drift, reconnects and pass-through on real hardware.

Original remotes already provide accelerometer pitch/roll and an IR camera; neither
requires MotionPlus. Gravity does not provide yaw. See the
[native acceptance checklist](docs/Platform-Support.md) for completion criteria.
