# License and notice audit

OpenWiimote-owned code is ISC. Copyright remains 2026 Matthew Hand.
`package.json` already declared ISC; .NET package metadata now declares ISC too.

## Scope and evidence

`third_party/manifest.json` inventories all 23 package libraries from the published
`OpenWiimote.deps.json`, not only direct project references. Each entry records the exact
NuGet version and SHA-512 package identity, package-declared license, notice documents and
available source revision. Full original texts are in `third_party/licenses/`, with SHA-256
and source/package provenance; their contents have not been abridged or relabelled ISC.

- Avalonia MIT and NOTICE are from the NuGet-declared source revision.
- Inter is separately OFL-1.1. All six embedded fonts have version
  `3.019;git-0a5106e0b`, names the Inter Project Authors, and identifies the SIL OFL 1.1.
  Exact bytes of all six fonts from the pinned Avalonia source appear in all three NuGet resource assemblies;
  the full OFL is taken from that Inter source revision. No fonts were changed.
- HidSharp's package contains its Apache-2.0 license and copyright notice; both are kept.
- SkiaSharp/HarfBuzzSharp license and full supplied native third-party notice collections
  are retained. All platform-package notice copies were compared; identical texts are
  shared without modifying them. ANGLE's package license is also included.
- MicroCom declares MIT but supplies no revision or full license in NuGet. The upstream
  LICENSE was last changed in its 2021 introduction commit; that exact text is included,
  with the provenance limitation recorded. ViGEm.NET and Tmds notices use package source
  revisions. The ViGEm client is distinct from the separately installed driver.
- The generated .NET app host is bundled. The reviewed Ubuntu SDK 10.0.114/runtime
  distribution 10.0.14 supplies the included .NET license and third-party notices. These
  are vendor-supplied files, not an inferred upstream tag. The Linux archive requires a system .NET runtime. Windows packages include a pinned
  official runtime with its original Windows runtime license and third-party notices;
  do not assume all runtime components use the application license. System drivers are not bundled.
- Playwright's Apache-2.0 LICENSE and NOTICE accompany the development dependency inventory.
  Node modules, downloaded browsers and .NET test-package binaries are not redistributed
  in the runtime archive; their installed packages retain their own license files.

The root notice document distinguishes package-level licenses from embedded components.
An upstream notice collection may mention components not used in a particular binary;
complete supplied collections are retained rather than trimmed based on assumptions.

## Offline release guard

Run the standard-library regressions with:

```sh
python3 -m unittest discover -s tests/licensing -p 'test_*.py'
```

`scripts/package-linux.sh` invokes `scripts/bundle-notices.py` after its offline publish.
The guard checks the actual dependency graph against reviewed versions and package hashes,
checks the published dependency files against the reviewed package bytes, checks npm lock
versions/integrities, validates every notice hash, verifies the ISC text,
and rejects an unreviewed SDK. All needed notice files are committed; no license fetch or
new dependency is required during packaging. The archive checksum manifest covers the
license bundle too, and release.json records ISC and the notice manifest hash.

When updating a dependency or the SDK, review its supplied upstream texts, add any embedded
component notices, update the provenance manifest, and rerun the guard and artifact checks.
Do not merely copy a new version number onto an old notice entry. Keep the complete bundle
with binary distributions. Public repository/history privacy review and physical hardware
acceptance remain separate release tasks.
