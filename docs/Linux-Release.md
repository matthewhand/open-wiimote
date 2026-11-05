# Linux release candidates

The package is a framework-dependent Linux build. It requires .NET 10 and the platform
permissions described in [Linux setup](Linux-Setup.md) (in an extracted package, see
`docs/Linux-Setup.md`). It includes the native app host `OpenWiimote` for compatibility
with existing service units, plus the `openwiimote` launcher. Both require .NET 10.
The bundle also contains `openwiimote-helper`, the normal-account BlueZ helper. The
application can find it beside the executable when no system-installed helper is selected;
no privileged helper is installed automatically. MotionPlus gyro is an unavailable
experimental stub.

## Build offline

Use a clean, committed source checkout with an already populated .NET package cache and
restore assets. Put artifacts outside the checkout. On a Linux x64 or arm64 build host:

```sh
./scripts/package-linux.sh 0.1.0-rc.2 /absolute/path/to/private-artifacts
```

The script does not restore dependencies, install software, touch controllers or change a
service. Missing cached dependencies fail the build. It disables telemetry/workload update
notifications; run it in a network-isolated environment when strict offline enforcement is
needed. Run the offline .NET/browser suites separately before packaging: a package is not
a substitute for tests. It packages the host architecture only, not a cross-compiled build.

The output is a versioned tarball and its SHA-256 file. The payload includes `release.json`
with the version, source commit and tree, target architecture, runtime requirement and
pending hardware acceptance. `SHA256SUMS` verifies the extracted files. Debug symbols,
settings, profiles, tokens, logs and repository metadata are excluded. The content check
rejects obvious private/configuration paths and symlinks; it is not a complete secret or
licensing audit. No archive from an earlier build is overwritten.

```sh
cd /absolute/path/to/private-artifacts
sha256sum -c openwiimote-0.1.0-rc.2-linux-x64.tar.gz.sha256
```

After extraction, enter the extracted version directory and run `sha256sum -c SHA256SUMS`.
Inspect `release.json` and `RELEASE-NOTES.md` before using the candidate.

## First installation

Keep the extracted version directory under `~/.local/share/openwiimote/releases/`, for
example `openwiimote-0.1.0-rc.2-linux-x64`. The user running the app owns this directory.
Do not put credentials inside a release directory. To run it directly:

```sh
cd ~/.local/share/openwiimote/releases/openwiimote-0.1.0-rc.2-linux-x64
./check-linux-setup.sh
./openwiimote --headless --urls http://127.0.0.1:8080
```

A token is generated and printed when no access token is configured. For a repeatable
service, create `~/.config/openwiimote/service.env` privately (`umask 077`) containing
`OPENWIIMOTE_ACCESS_TOKEN` and `OPENWIIMOTE_URLS`. Use localhost unless LAN access is
intentional; keep authentication enabled. Check that no parent directory has an unwanted
`.env`, because the application supports loading a nearby development environment file.

A first-install service example is in `packaging/openwiimote.service.example`. Point
`~/.local/share/openwiimote/current` at the extracted version directory. If no
`~/.config/systemd/user/openwiimote.service` exists, copy the example there, run
`systemctl --user daemon-reload`, then `systemctl --user enable --now openwiimote.service`.
Read the example first. It expects `~/.config/openwiimote/service.env` to exist. Installing
this service does not load kernel modules, install udev rules or add account memberships.

If a service already exists, preserve it and inspect its working directory, executable and
environment file. Do not replace it blindly with the example. Existing units calling the
`OpenWiimote` app host can continue using it; units calling `openwiimote` use the launcher.

## Upgrade and rollback

Keep the previous version directory. Stop `openwiimote.service` before switching releases
so it can neutralize output and save settings. Make a private backup of `settings.json`,
`Profiles/` and the service environment, and record the current symlink target. Preserve
these backups on durable storage, not solely in disposable build scratch.

With the service stopped, verify the new archive and extracted file checksums. Create a
new symlink beside `current` and atomically rename that symlink over `current`. Confirm
that `current` is a symlink before replacing it; do not replace an ordinary directory. Then
start the service. Check `/api/health`, authenticate, inspect live reports and verify
virtual-controller creation before a game session. The operator's emulation setting may
be off; enable it deliberately for output testing.

If the new build fails, stop it and switch `current` back to the recorded previous release,
then start it again. Do not automatically restore the old settings backup: retain any newer
user changes first. This release preserves unknown settings fields for compatibility.

## Before public distribution

OpenWiimote-owned source uses ISC. The archive includes `LICENSE`,
`THIRD-PARTY-NOTICES.md` and `third_party/` with the complete reviewed upstream texts and
provenance manifest. Retain this bundle when distributing the binaries. The Inter font
and native components retain their separate upstream terms; the ISC license does not
relicense them. See `Licensing.md` in the source tree for the audit and update procedure.

The packager performs notice validation offline after publishing: unknown package
versions/hashes, changed development dependencies, missing or altered notices and an
unreviewed .NET SDK cause failure. Packaging currently expects the audited SDK 10.0.114;
review its supplied license/notices when updating the SDK. These checks also run without
GitHub CI. Source-history/private-data review and the release's hardware acceptance are
separate from license integration. Preparing a candidate does not change repository
visibility or publish a release.

Physical acceptance remains incomplete: two-remotes button/tilt checks, a flat neutral
pose, physical Bluetooth reconnects, and a normal game session must still be recorded.
The application-level reconnect passed for one remote; tilt movement was observed and
reported as looking good by the user. Those observations do not establish the full
hardware checklist or behavior on Windows/macOS.
