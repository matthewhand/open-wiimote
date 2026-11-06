# Release quality checklist

rc.11 is a prerelease with one confirmed real Windows ARM IR mouse session. The native
acceptance matrix is in [Platform-Support.md](Platform-Support.md).

- Preserve saved Bluetooth bonds and neutralize active output on loss, shutdown or handover.
- Never let button/status reports refresh stale IR; require exactly two plausible lights
  for pointer capture and retain diagnostic visibility of missing/extra dots.
- Keep mouse output off during calibration and startup. Save calibration per remote/display,
  reject invalid or unstable targets, and keep previous calibration on cancellation/failure.
- Verify independent profiles, player assignments and output for two remotes.
- Run .NET, browser/API, calibration math, report replay and licensing checks locally.
- Review actual runtime/package hashes and retain all original license and notice texts.
- Review public source, documentation and package metadata for private settings, identifiers,
  local paths, credentials and private development ancestry before publishing.
- Record physical acceptance honestly. Passing simulated tests does not prove Bluetooth,
  sensor geometry, driver installation or normal gameplay.
- Keep GitHub Actions off and publish source snapshots with public-only ancestry.

Future platform and MotionPlus work is tracked in [the roadmap](../ROADMAP.md).
