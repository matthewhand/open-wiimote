import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / 'scripts/bundle-notices.py'
spec = importlib.util.spec_from_file_location('bundle_notices', SCRIPT)
notices = importlib.util.module_from_spec(spec)
spec.loader.exec_module(notices)


class NoticeBundleTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / 'third_party/licenses').mkdir(parents=True)
        (self.root / 'LICENSE').write_text('ISC fixture')
        (self.root / 'THIRD-PARTY-NOTICES.md').write_text('Dependency fixture')
        (self.root / 'third_party/licenses/Dependency.txt').write_text('Full upstream notice fixture')
        self.manifest = {
            'schemaVersion': 1,
            'projectLicense': {'spdx': 'ISC', 'sha256': self.hash(self.root / 'LICENSE')},
            'appHost': {'sdkVersion': '10.0.test', 'documents': ['Dependency.txt']},
            'documents': {'Dependency.txt': {'sha256': self.hash(self.root / 'third_party/licenses/Dependency.txt')}},
            'packages': {'Dependency/1.0': {'sha512': 'sha512-reviewed', 'documents': ['Dependency.txt'], 'files': {}}},
            'developmentPackages': {}
        }
        (self.root / 'package-lock.json').write_text(json.dumps({'packages': {'': {'version': '1'}}}))
        self.deps = {'libraries': {'Dependency/1.0': {'type': 'package', 'sha512': 'sha512-reviewed'},
                                   'OpenWiimote/1.0': {'type': 'project'}}}
        self.save()

    @staticmethod
    def hash(path):
        return hashlib.sha256(path.read_bytes()).hexdigest()

    def save(self):
        (self.root / 'third_party/manifest.json').write_text(json.dumps(self.manifest))
        (self.root / 'deps.json').write_text(json.dumps(self.deps))

    def bundle(self):
        self.save()
        return notices.bundle(self.root, self.root / 'deps.json', self.root / 'output', '10.0.test')

    def test_release_contains_complete_original_license_texts(self):
        self.assertEqual(1, self.bundle())
        for name in ['LICENSE', 'THIRD-PARTY-NOTICES.md', 'third_party/licenses/Dependency.txt']:
            self.assertEqual((self.root / name).read_bytes(), (self.root / 'output' / name).read_bytes())

    def test_new_runtime_package_is_rejected_before_output_is_written(self):
        self.deps['libraries']['Unknown/2.0'] = {'type': 'package', 'sha512': 'sha512-other'}
        with self.assertRaisesRegex(notices.NoticeError, 'Unreviewed release dependency'):
            self.bundle()
        self.assertFalse((self.root / 'output').exists())

    def test_altered_package_hash_is_rejected(self):
        self.deps['libraries']['Dependency/1.0']['sha512'] = 'sha512-changed'
        with self.assertRaisesRegex(notices.NoticeError, 'Package hash mismatch'):
            self.bundle()

    def test_missing_or_changed_notice_is_rejected(self):
        p = self.root / 'third_party/licenses/Dependency.txt'
        p.write_text('Abridged notice')
        with self.assertRaisesRegex(notices.NoticeError, 'Notice hash mismatch'):
            self.bundle()
        p.unlink()
        with self.assertRaisesRegex(notices.NoticeError, 'Missing or symlinked'):
            self.bundle()

    def test_unreviewed_sdk_is_rejected(self):
        with self.assertRaisesRegex(notices.NoticeError, 'Unreviewed .NET SDK'):
            notices.validate(self.root, self.deps, 'new-sdk')

    def test_changed_development_dependencies_are_rejected(self):
        (self.root / 'package-lock.json').write_text(json.dumps({'packages': {'node_modules/new': {'version': '2', 'integrity': 'new'}}}))
        with self.assertRaisesRegex(notices.NoticeError, 'Development dependencies changed'):
            self.bundle()

    def test_notice_cannot_escape_its_directory(self):
        (self.root / 'third_party/outside.txt').write_text('Outside')
        self.manifest['documents']['../outside.txt'] = {'sha256': self.hash(self.root / 'third_party/outside.txt')}
        with self.assertRaisesRegex(notices.NoticeError, 'escapes the bundle'):
            self.bundle()

    def test_changed_published_dependency_bytes_are_rejected(self):
        output = self.root / 'output'
        output.mkdir()
        (output / 'Dependency.dll').write_bytes(b'reviewed binary')
        self.manifest['packages']['Dependency/1.0']['files'] = {'Dependency.dll': self.hash(output / 'Dependency.dll')}
        (output / 'Dependency.dll').write_bytes(b'different binary')
        with self.assertRaisesRegex(notices.NoticeError, 'Published dependency file differs'):
            self.bundle()
        self.assertFalse((output / 'LICENSE').exists())

    def test_changed_project_license_is_rejected(self):
        (self.root / 'LICENSE').write_text('Different project terms')
        with self.assertRaisesRegex(notices.NoticeError, 'Project LICENSE differs'):
            self.bundle()


if __name__ == '__main__':
    unittest.main()
