"""Bounded offline regressions for notice provenance; no restore/build/runtime."""
import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def load(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts' / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


go = load('collect-go-notices')
nuget = load('collect-notices')


def sha(data):
    return hashlib.sha256(data).hexdigest()


class ProvenanceTests(unittest.TestCase):
    def test_go_keeps_nested_originals_and_validates_binary_h1(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            files = {'example.org/module@v1.0.0/LICENSE': b'root BSD',
                     'example.org/module@v1.0.0/nested/COPYING': b'other copyright MIT',
                     'example.org/module@v1.0.0/nested/source.go': b'package nested\n'}
            archive = root / 'module.zip'
            with zipfile.ZipFile(archive, 'w') as z:
                for n, b in files.items():
                    z.writestr(n, b)
            # Known Go Hash1 construction, independent of notice filtering.
            lines = ''.join(sha(b) + '  ' + n + '\n' for n, b in sorted(files.items()))
            h1 = 'h1:' + base64.b64encode(hashlib.sha256(lines.encode()).digest()).decode()
            out = root / 'out'
            records, evidence = go.collect_module_archive(archive, 'example.org/module', 'v1.0.0', h1, out, out / 'module')
            self.assertEqual(len(records), 2)
            self.assertEqual((out / 'module/nested/COPYING').read_bytes(), b'other copyright MIT')
            self.assertEqual(evidence['goSum'], h1)
            with self.assertRaisesRegex(RuntimeError, 'checksum'):
                go.collect_module_archive(archive, 'example.org/module', 'v1.0.0', 'h1:WRONG', out, out / 'bad')
            self.assertFalse((out / 'bad').exists())

    def test_go_rejects_archive_traversal(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            archive = root / 'bad.zip'
            with zipfile.ZipFile(archive, 'w') as z:
                z.writestr('example.org/module@v1.0.0/../LICENSE', b'escape')
            with self.assertRaisesRegex(RuntimeError, 'Unsafe'):
                go.collect_module_archive(archive, 'example.org/module', 'v1.0.0', 'h1:any', root / 'out', root / 'out/mod')
            self.assertFalse((root / 'out').exists())

    def test_nuget_distinguishes_signed_archive_hash_from_restore_hash(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            archive = root / 'fixture.1.0.0.nupkg'
            dll = b'headerFONTfooter'
            (root / 'font.dll').write_bytes(dll)
            with zipfile.ZipFile(archive, 'w') as z:
                z.writestr('font.dll', dll)
            raw = archive.read_bytes()
            pin = {'id': 'Fixture', 'version': '1.0.0', 'nupkgSha256': sha(raw),
                   'nupkgSha512': base64.b64encode(hashlib.sha512(raw).digest()).decode(),
                   'restoreContentHash': 'independently-pinned-restore-content-hash',
                   'publishedFiles': [], 'cacheFiles': [{'path': 'font.dll', 'archivePath': 'font.dll', 'sha256': sha(dll)}],
                   'embeddedFonts': [{'name': 'Font', 'container': 'font.dll', 'offset': 6, 'size': 4, 'sha256': sha(b'FONT')}]}
            nuget.verified_supplement(root, pin, {pin['restoreContentHash']})
            with self.assertRaisesRegex(RuntimeError, 'lock hash'):
                nuget.verified_supplement(root, pin, {'changed-lock'})
            (root / 'font.dll').write_bytes(b'edited')
            with self.assertRaisesRegex(RuntimeError, 'input changed'):
                nuget.verified_supplement(root, pin, {pin['restoreContentHash']})
            (root / 'font.dll').write_bytes(dll)
            pin['embeddedFonts'][0]['offset'] = 0
            with self.assertRaisesRegex(RuntimeError, 'Embedded font'):
                nuget.verified_supplement(root, pin, {pin['restoreContentHash']})
            archive.write_bytes(raw + b'changed')
            with self.assertRaisesRegex(RuntimeError, 'package integrity'):
                nuget.verified_supplement(root, pin, {pin['restoreContentHash']})

    def test_every_supplement_text_is_hash_pinned_and_mapped(self):
        root = ROOT / 'packaging/licenses'
        entries = {e['file']: e for e in json.loads((root / 'sources.json').read_text())['files']}
        supplement = json.loads((root / 'supplemental.json').read_text())
        for entry in entries.values():
            self.assertEqual(sha((root / entry['file']).read_bytes()), entry['sha256'], entry['file'])
        for pin in supplement['packages']:
            self.assertTrue(pin['cacheFiles'])
            for name in pin['notices']:
                self.assertIn(name, entries)
        yaml = next(p for p in supplement['packages'] if p['id'] == 'YamlDotNet')
        self.assertIn('YamlDotNet-16.3.0-LICENSE-libyaml', yaml['notices'])
        inter = next(p for p in supplement['packages'] if p['id'] == 'Avalonia.Fonts.Inter')
        self.assertEqual(len(inter['embeddedFonts']), 6)


if __name__ == '__main__':
    unittest.main()
