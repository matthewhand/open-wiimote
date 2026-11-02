# Original Wii Remote release candidate

Status: public prerelease **0.1.0-rc.2**, Linux x64. Hardware acceptance remains partial.
ISC and full dependency notices are included. See `PUBLIC-RELEASE-PROVENANCE.json` for the
reviewed source export and binary build provenance.

## Included

- Buttons and accelerometer tilt mapped to a virtual Xbox-style controller.
- A dashboard setup card distinguishing connection, backend permissions, emulation state,
  missing controller output and service connection loss.
- Profile assignment and save protections, including filename collision checks, preserved
  remote pins after rename/delete, and retention of unknown settings during upgrades.
- Watcher shutdown and canceled-open cleanup, plus desktop controller failure/replacement recovery.
- Bluetooth reconnects retain existing bonds and report success only after connection is verified.
- Device tabs follow reconnects; profile changes reload editors; hung or malformed state replies clear readiness.
- Avatar levelling is explicitly browser-only and leaves live/controller tilt readings unchanged.
- Offline regression coverage: 258 .NET tests and 51 browser/API tests passed.
- Read-only Linux setup diagnostics and a versioned package with manifests/checksums.

## Licensing

OpenWiimote-owned source is ISC. The archive includes the reviewed notice bundle for
all 23 runtime NuGet dependencies, Avalonia embedded Inter fonts, native components and
the generated .NET app host. Dependency versions, package hashes and notice hashes are
checked offline during packaging. Upstream terms continue to apply to those components.

## Compatibility and limitations

MotionPlus gyro support remains experimental and unavailable. Existing profile fields
remain compatible, but the stub supplies no gyro readings. Linux kernel-owned extension
handling, Windows hardware behavior and macOS virtual output have their documented limits;
macOS has no implemented virtual-controller backend.

IR-mouse/display additions are outside this release. No kernel patch is required.

Dashboard action routes require POST, including scan, disconnect, profile reload and log
clearing. External clients that previously used GET for those actions must update.

## Acceptance still required

A user reported tilt looking good, one virtual stick axis was observed moving, and one
application reconnect restored its profile and controller. A prior button recording window
expired without capturing the sequence. Both remotes are not yet accepted together;
physical Bluetooth reconnects, neutral flat pose, full pitch and a game session remain
unverified. Passing synthetic tests must not be described as completing these checks.
