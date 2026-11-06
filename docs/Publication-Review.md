# rc.11 publication review

The public release uses a reviewed source snapshot parented only on existing public
history. Development commits, local configuration, agent handoffs, raw hardware logs,
credentials and GitHub workflow definitions are excluded from the new public snapshot.

Known physical device addresses in fixtures were replaced with synthetic locally
administered addresses, local host references were generalized, and project links point
to the public repository. Included files were scanned for known private identifiers,
local developer paths and credential patterns. Original dependency notices are retained.

Builds identify their public source commit in `release.json`. Packaging uses reviewed
local caches and verifies dependency/runtime identities and complete license notices.
The release remains a prerelease with the hardware limits in Platform-Support.md.
GitHub Actions remains disabled; verification and packaging run locally.
