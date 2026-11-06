#!/usr/bin/env bash
# Prepare a framework-dependent release candidate from committed sources.
# No restore, network calls, installation, service changes or hardware access.
set -euo pipefail
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1

usage() {
  echo 'Usage: scripts/package-linux.sh VERSION OUTPUT_DIRECTORY' >&2
  echo 'Example: scripts/package-linux.sh 0.1.0-rc.1 /path/to/private-artifacts' >&2
}
[[ $# == 2 ]] || { usage; exit 2; }
version=$1
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$ ]] || {
  echo 'Version must be a numeric major.minor.patch, optionally with a prerelease suffix.' >&2; exit 2;
}
[[ $(uname -s) == Linux ]] || { echo 'Build the Linux candidate on Linux.' >&2; exit 2; }
case $(uname -m) in
  x86_64) arch=x64 ;;
  aarch64|arm64) arch=arm64 ;;
  *) echo 'Supported packaging hosts are Linux x64 and arm64.' >&2; exit 2 ;;
esac
for command in git dotnet python3 tar gzip sha256sum realpath; do
  command -v "$command" >/dev/null || { echo "Missing prerequisite: $command" >&2; exit 2; }
done
repo=$(git -C "$(dirname "$0")/.." rev-parse --show-toplevel)
cd "$repo"
git diff --quiet && git diff --cached --quiet || {
  echo 'Commit or isolate tracked source changes before packaging; the manifest must describe the payload.' >&2; exit 2;
}
# Untracked compile/content inputs would not be represented by the source commit.
python3 - <<'PY'
import subprocess,sys
paths=subprocess.check_output(['git','ls-files','--others','--exclude-standard','-z']).decode().split('\0')
bad=[p for p in paths if p and ((p.startswith('src/') and not any(part in ('bin','obj') for part in p.split('/'))) or
     ('/' not in p and (p.endswith(('.props','.targets','.csproj')) or p in ('global.json','NuGet.config'))))]
if bad:
    print('Untracked source/content inputs must be committed or removed before packaging.',file=sys.stderr)
    sys.exit(2)
PY
for input in LICENSE THIRD-PARTY-NOTICES.md third_party/manifest.json scripts/bundle-notices.py scripts/package-linux.sh scripts/check-linux-setup.sh tools/openwiimote-helper.sh docs/Linux-Release.md docs/Release-Notes.md docs/Avatar-Calibration.md docs/Linux-Setup.md docs/Licensing.md packaging/60-openwiimote.rules packaging/openwiimote.service.example; do
  git ls-files --error-unmatch -- "$input" >/dev/null 2>&1 || {
    echo "Commit packaging input before building: $input" >&2; exit 2;
  }
done
python3 - <<'CHECK_LICENSE_INPUTS'
import json,subprocess
from pathlib import Path
m=json.loads(Path('third_party/manifest.json').read_text())
for name in m['documents']:
    subprocess.run(['git','ls-files','--error-unmatch','--','third_party/licenses/'+name],check=True,stdout=subprocess.DEVNULL)
CHECK_LICENSE_INPUTS
output_dir=$(realpath -m -- "$2")
[[ $output_dir != / ]] || { echo 'Choose a dedicated artifact directory.' >&2; exit 2; }
case "$output_dir/" in "$repo/"*) echo 'Put release artifacts outside the source checkout.' >&2; exit 2 ;; esac
mkdir -p -- "$output_dir"
name="openwiimote-$version-linux-$arch"
archive="$output_dir/$name.tar.gz"
[[ ! -e $archive && ! -e $archive.sha256 ]] || { echo 'That release artifact already exists; choose a new version or directory.' >&2; exit 2; }
stage=$(mktemp -d "$output_dir/.openwiimote-package.XXXXXXXX")
cleanup() {
  if [[ -n ${stage:-} && $stage == "$output_dir"/.openwiimote-package.* ]]; then rm -rf -- "$stage"; fi
}
trap cleanup EXIT
payload="$stage/$name"
commit=$(git rev-parse HEAD)
tree=$(git rev-parse 'HEAD^{tree}')
epoch=$(git show -s --format=%ct HEAD)
# Existing restore assets and the local package cache are required. A missing cache
# causes failure; this script never restores dependencies or installs workloads.
dotnet publish src/OpenWiimote.App/OpenWiimote.App.csproj -c Release --no-restore \
  --disable-build-servers -m:1 --no-self-contained -p:UseAppHost=true \
  -p:Version="$version" -p:SourceRevisionId="$commit" -p:DebugType=None -p:DebugSymbols=false \
  -o "$payload"
python3 scripts/bundle-notices.py --deps "$payload/OpenWiimote.deps.json" \
  --output "$payload" --sdk-version "$(dotnet --version)"
# Debug symbols can contain developer paths and are not part of the candidate.
find "$payload" -type f -name '*.pdb' -delete
mkdir -p "$payload/docs" "$payload/packaging"
cp docs/Linux-Release.md docs/Linux-Setup.md docs/Licensing.md docs/Avatar-Calibration.md docs/Pointer-Setup.md "$payload/docs/"
cp docs/Release-Notes.md "$payload/RELEASE-NOTES.md"
cat > "$payload/README.md" <<'PACKAGE_README'
# OpenWiimote Linux prerelease

The version and source commit are in `release.json`. This framework-dependent package
requires .NET 10. Verify the extracted files with `sha256sum -c SHA256SUMS`, then run:

```sh
./check-linux-setup.sh
./openwiimote --headless --urls http://127.0.0.1:8080
```

Read [setup](docs/Linux-Setup.md), [installation and rollback](docs/Linux-Release.md), and
[release notes](RELEASE-NOTES.md). Keep credentials and profiles outside this directory.
The service example is in `packaging/openwiimote.service.example`; do not overwrite an
existing service blindly. MotionPlus gyro is experimental and unavailable. Hardware
acceptance remains incomplete. OpenWiimote source uses ISC; retain LICENSE and the full
[third-party notices](THIRD-PARTY-NOTICES.md) when distributing this archive.
PACKAGE_README
cp scripts/check-linux-setup.sh "$payload/"
cp tools/openwiimote-helper.sh "$payload/openwiimote-helper"
cp packaging/60-openwiimote.rules packaging/openwiimote.service.example "$payload/packaging/"
cat > "$payload/openwiimote" <<'LAUNCHER'
#!/usr/bin/env bash
set -euo pipefail
release_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$release_dir"
exec dotnet "$release_dir/OpenWiimote.dll" "$@"
LAUNCHER
chmod +x "$payload/openwiimote" "$payload/check-linux-setup.sh" "$payload/openwiimote-helper"
python3 - "$payload" "$version" "$commit" "$tree" "$arch" <<'PY'
from pathlib import Path
import json,sys,hashlib
root=Path(sys.argv[1])
manifest={'version':sys.argv[2],'sourceCommit':sys.argv[3],'sourceTree':sys.argv[4],
          'platform':'linux','architecture':sys.argv[5],'packageType':'framework-dependent',
          'requires':'.NET 10 runtime','hardwareAcceptance':'pending',
          'sourceLicense':'ISC','thirdPartyNoticeManifest':'third_party/manifest.json',
          'thirdPartyNoticeManifestSha256':hashlib.sha256((root/'third_party/manifest.json').read_bytes()).hexdigest(),
          'motionPlus':{'status':'experimental','available':False}}
(root/'release.json').write_text(json.dumps(manifest,indent=2)+'\n')
required=['OpenWiimote','OpenWiimote.dll','OpenWiimote.runtimeconfig.json','OpenWiimote.deps.json','wwwroot/index.html','openwiimote','openwiimote-helper','LICENSE','THIRD-PARTY-NOTICES.md','third_party/manifest.json']
assert all((root/p).is_file() for p in required),'Missing required runtime file'
for p in root.rglob('*'):
    relative=p.relative_to(root)
    if p.is_symlink(): raise SystemExit('Package contains a symlink: '+str(relative))
    if any(part in {'.git','node_modules','Profiles','Logs','backups','hardware-test'} for part in relative.parts):
        raise SystemExit('Package contains an unintended data directory: '+str(relative))
    if p.is_file() and (p.name.startswith('.env') or p.name in {'settings.json','service.env'} or p.suffix in {'.log','.pdb'}):
        raise SystemExit('Package contains a private or diagnostic file: '+str(relative))
PY
(cd "$payload" && find . -type f ! -name SHA256SUMS -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS)
tar -C "$stage" --sort=name --mtime="@$epoch" --owner=0 --group=0 --numeric-owner -cf - "$name" | gzip -n > "$stage/artifact.tar.gz"
(cd "$stage" && sha256sum artifact.tar.gz | sed "s/  artifact.tar.gz$/  $name.tar.gz/") > "$stage/artifact.sha256"
# Hard links create the output atomically without overwriting an existing release.
ln -- "$stage/artifact.tar.gz" "$archive"
ln -- "$stage/artifact.sha256" "$archive.sha256"
printf 'Private release candidate: %s\n' "$archive"
printf 'Verify with: (cd %q && sha256sum -c %q)\n' "$output_dir" "$name.tar.gz.sha256"
printf 'Hardware acceptance and public distribution review remain required.\n'
