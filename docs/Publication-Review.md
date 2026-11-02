# rc.2 publication review

The public repository begins with a reviewed source snapshot. The existing private
repository, its history, pull requests and discussions remain private.

## Privacy scope

The review inspected all reachable objects in the private source repository (748 unique
objects; 35 commits on its main development branch), searching for credentials, private
keys, local network addresses, home paths and configuration artifacts. Credential matches
were documented example/test values, not live credentials. This is a targeted review,
not a guarantee that pattern matching finds every possible secret.

The public snapshot excludes the local `.env` and the GitHub Actions workflow. A
host-specific diagnostic address now defaults to localhost. Hardware identifiers in
examples and tests use synthetic addresses. Host names and private repository links
were removed. Public commits use the maintainer's GitHub no-reply address. There are no
prior commits or private PR discussions in the public repository.

All 64 files under `src/` match the reviewed application source. The public binary is
built from the public snapshot; its `release.json` records that exact source commit and
tree. Build outputs, settings, profiles, logs and credentials are excluded. The archive
includes file checksums, the ISC license and the complete reviewed third-party notices.

## Release gates

- Build, .NET tests, browser/API tests, replay and licensing regressions run locally with
  network access disabled; no hosted CI is used.
- Packaging verifies the reviewed dependency hashes and notice texts. The extracted
  archive is checked and smoke-tested separately.
- GitHub Actions is disabled before the first push. The public snapshot has no Actions
  workflow. Release publication does not enable it.
- This is a prerelease: physical acceptance remains partial as described in
  [release notes](Release-Notes.md). MotionPlus is an unavailable experimental stub.
