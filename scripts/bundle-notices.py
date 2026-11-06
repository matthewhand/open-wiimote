#!/usr/bin/env python3
"""Validate the reviewed dependency/notice inventory and copy it into a release. No network."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys


class NoticeError(ValueError):
    pass


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def safe_document(root, name):
    path = root / name
    if path.is_symlink() or not path.is_file():
        raise NoticeError('Missing or symlinked notice: ' + name)
    try:
        path.resolve().relative_to(root.resolve())
    except ValueError:
        raise NoticeError('Notice path escapes the bundle: ' + name) from None
    return path


def validate(repo, deps, sdk_version):
    manifest = json.loads((repo / 'third_party/manifest.json').read_text())
    if manifest.get('schemaVersion') != 1:
        raise NoticeError('Unknown notice manifest schema')
    if manifest['appHost']['sdkVersion'] != sdk_version:
        raise NoticeError('Unreviewed .NET SDK: ' + sdk_version)
    if manifest['projectLicense']['spdx'] != 'ISC' or digest(repo / 'LICENSE') != manifest['projectLicense']['sha256']:
        raise NoticeError('Project LICENSE differs from the reviewed ISC text')
    for name, info in manifest['documents'].items():
        if digest(safe_document(repo / 'third_party/licenses', name)) != info['sha256']:
            raise NoticeError('Notice hash mismatch: ' + name)
    own_projects = {'OpenWiimote', 'OpenWiimote.Core', 'OpenWiimote.Platform.Linux',
                    'OpenWiimote.Platform.Windows', 'OpenWiimote.Platform.MacOS'}
    used = set(manifest['appHost']['documents'])
    runtime_packages = 0
    for name, info in deps['libraries'].items():
        if info['type'] == 'project' and name.rsplit('/', 1)[0] in own_projects:
            continue
        reviewed = manifest['packages'].get(name)
        if info['type'] != 'package' or reviewed is None:
            raise NoticeError('Unreviewed release dependency: ' + name)
        if not info.get('sha512') or info['sha512'] != reviewed['sha512']:
            raise NoticeError('Package hash mismatch: ' + name)
        used.update(reviewed['documents'])
        runtime_packages += 1
    if not used or not used.issubset(manifest['documents']):
        raise NoticeError('Dependency refers to a missing notice document')
    lock = json.loads((repo / 'package-lock.json').read_text())
    development = {}
    for name, info in lock['packages'].items():
        if not name:
            continue
        key = name.removeprefix('node_modules/') + '/' + info['version']
        development[key] = info.get('integrity')
    expected = {name: info['integrity'] for name, info in manifest['developmentPackages'].items()}
    if development != expected:
        raise NoticeError('Development dependencies changed; review their notices')
    for info in manifest['developmentPackages'].values():
        if not set(info['documents']).issubset(manifest['documents']):
            raise NoticeError('Development dependency refers to a missing notice')
    return manifest, runtime_packages


def bundle(repo, deps_file, output, sdk_version, rid=None):
    manifest, count = validate(repo, json.loads(deps_file.read_text()), sdk_version)
    deps = json.loads(deps_file.read_text())
    if rid is not None and (rid not in ('win-x64', 'win-arm64') or not deps.get('runtimeTarget', {}).get('name', '').endswith('/' + rid)):
        raise NoticeError('Runtime identifier does not match the published dependency graph')
    for name, info in deps['libraries'].items():
        if info['type'] != 'package':
            continue
        for relative, expected in manifest['packages'][name]['files'].items():
            published = relative
            if rid is not None and relative.startswith('runtimes/'):
                parts = relative.split('/')
                if len(parts) < 4 or parts[1] != rid:
                    continue
                published = parts[-1]
            actual = safe_document(output, published)
            if digest(actual) != expected:
                raise NoticeError('Published dependency file differs from reviewed package: ' + relative)
    # All inputs are validated before creating or changing the output directory.
    output.mkdir(parents=True, exist_ok=True)
    for name in ('LICENSE', 'THIRD-PARTY-NOTICES.md'):
        shutil.copyfile(repo / name, output / name)
    destination = output / 'third_party'
    (destination / 'licenses').mkdir(parents=True, exist_ok=True)
    shutil.copyfile(repo / 'third_party/manifest.json', destination / 'manifest.json')
    for name in manifest['documents']:
        target = destination / 'licenses' / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(repo / 'third_party/licenses' / name, target)
    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--deps', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--sdk-version', required=True)
    parser.add_argument('--rid', choices=('win-x64', 'win-arm64'))
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    try:
        count = bundle(args.repo, args.deps, args.output, args.sdk_version, args.rid)
    except (NoticeError, OSError, KeyError, json.JSONDecodeError) as error:
        print('Notice validation failed: ' + str(error), file=sys.stderr)
        return 2
    print('Bundled reviewed license/notices for ' + str(count) + ' runtime packages.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
