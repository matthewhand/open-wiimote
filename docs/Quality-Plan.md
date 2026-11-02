# Quality plan for original Wii Remotes

The target is dependable buttons, tilt, profiles, reconnects and virtual-controller output.
MotionPlus is outside the supported feature set. A subjective 4.5/5 rating requires a
successful physical play session as well as automated evidence.

## Implemented and covered offline

- Dashboard setup guidance distinguishes Bluetooth connection, controller permissions,
  emulation off, missing output, active output and service loss. It links to the existing
  controller/profile controls and works on a narrow screen.
- Action endpoints require POST. Malformed profile creation and unknown numeric button
  mappings cannot change a saved profile. Profile filename collisions cannot overwrite
  another profile, and overlapping saves use separate temporary files. Renamed/deleted
  profiles update remote pins; unknown settings from other builds survive saving.
- Pinned device profiles survive enabling emulation and selecting an active profile.
  Replaced connections cannot remove or feed input into a replacement session. Controllers
  receive neutral output on release. Output failures are visible and can be retried.
- MotionPlus has an explicit experimental stub that is always unavailable and supplies
  no samples. The old, unverified gyro-register polling has been removed. Existing profile
  fields and extension decoding remain for compatibility; draft PR #17 retains research.
- Linux has read-only diagnostics and setup/recovery instructions in `Linux-Setup.md`.

## Validation and integration

PR #18 validation passed 246 .NET tests and 46 browser tests using isolated synthetic
devices and profiles. It was merged and installed as a separate release on the Linux test host. The Linux diagnostic also passed on the Linux test host without modifying it.
The next candidate is prepared with `scripts/package-linux.sh`; see `Linux-Release.md`
for versioning, package checks, installation and rollback. Hosted CI remains intentionally unused.
The follow-up fixes passed 258 .NET tests and 51 browser tests offline. They cover watcher
shutdown races, preserved Bluetooth bonds, desktop output recovery, device-tab refresh,
focused-editor profile changes and hung/malformed state responses.
Private candidate preparation does not authorize public publication or a kernel-driver change.

## Remaining physical acceptance

Use the checklist in `Linux-Setup.md` with the updated build, both real remotes, and a game.
One app-level reconnect restored its profile and output; one stick axis moved and the user
reported tilt looking good. A full button recording, neutral pose, pitch, physical Bluetooth
reconnect and two-remotes game session remain unverified. The temporary Hardware Test profile was archived and removed, and the original Default
profile and device pins were restored. Emulation remains enabled.
Physical button/tilt feel, Bluetooth range, LEDs, rumble, and game integration cannot be
established by the synthetic suites. They remain unverified until that session is complete.
Windows and macOS behavior also require platform-specific acceptance; macOS currently
has no virtual-controller backend. Do not claim a cross-platform 4.5/5 result from Linux tests.

## License and notice integration

The existing owner-provided ISC license is tracked at the source root and included in
Linux archives. Full dependency notices and provenance are recorded in
`THIRD-PARTY-NOTICES.md` and `third_party/manifest.json`. The package step rejects
unreviewed dependencies, changed notice text and unreviewed SDKs. License integration
does not complete the remaining physical acceptance or a source-history privacy review.
