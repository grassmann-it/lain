"""Synthetic fixtures only; never execute fixture binaries or qualify a platform."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ENGINE = Path(__file__).resolve().parents[2] / 'packaging/platform-installers/install.py'
spec = importlib.util.spec_from_file_location('installer', ENGINE)
i = importlib.util.module_from_spec(spec)
spec.loader.exec_module(i)


def seal(root):
    files = {p.relative_to(root).as_posix(): i.digest(p) for p in root.rglob('*')
             if p.is_file() and p.name not in ('SHA256SUMS', 'native-qualification.json')}
    (root / 'native-qualification.json').write_text(json.dumps({
        'schemaVersion': 1, 'runtime': json.loads((root / 'platform-package.json').read_text())['runtime'],
        'passed': True, 'secretScanPassed': True, 'corePortable': True,
        'checks': sorted(i.CHECKS), 'files': files}))
    (root / 'SHA256SUMS').write_text(''.join(i.digest(p) + '  ' + p.relative_to(root).as_posix() + '\n'
        for p in sorted(root.rglob('*')) if p.is_file() and p.name != 'SHA256SUMS'))


def fixture(root, target='macos', mode='gateway', arch='x64'):
    root.mkdir()
    rid = ('osx' if target == 'macos' else 'win') + '-' + arch
    (root / 'platform-package.json').write_text(json.dumps({'schemaVersion': 1, 'runtime': rid, 'runtimeMode': mode}))
    names = ['cli/Lain.Cli', 'host/Lain.Host', 'desktop/Lain.Desktop']
    if mode == 'gateway':
        names.append('proxy/lain-proxy')
    for name in names:
        p = root / (name + ('.exe' if target == 'windows' else ''))
        p.parent.mkdir(exist_ok=True)
        if target == 'macos':
            data = b'\xcf\xfa\xed\xfe' + (0x01000007 if arch == 'x64' else 0x0100000c).to_bytes(4, 'little')
        else:
            data = b'MZ' + bytes(58) + (64).to_bytes(4, 'little') + b'PE\0\0' + (b'\x64\x86' if arch == 'x64' else b'\x64\xaa')
        p.write_bytes(data)
        p.chmod(0o700)
    (root / 'LICENSE').write_text('Synthetic license')
    (root / 'NOTICE').write_text('Synthetic fixture: NOT A NATIVE PACKAGE')
    (root / 'licenses').mkdir()
    (root / 'licenses/synthetic.txt').write_text('Synthetic fixture')
    seal(root)
    return root


class Contract(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.base = Path(self.temp.name)
        self.root = fixture(self.base / 'package')

    def tearDown(self):
        self.temp.cleanup()

    def test_matrix(self):
        for target in ('macos', 'windows'):
            for arch in ('x64', 'arm64'):
                for mode in ('monitor', 'gateway'):
                    with self.subTest(target=target, arch=arch, mode=mode):
                        p = fixture(self.base / (target + arch + mode), target, mode, arch)
                        self.assertEqual(i.validate(p, target, arch)['runtimeMode'], mode)

    def test_inventory_attacks(self):
        original = (self.root / 'SHA256SUMS').read_text()
        attacks = [original + original.splitlines()[0] + '\n',
                   original + '0' * 64 + '  ../outside\n',
                   original + '0' * 64 + '  ./NOTICE\n',
                   original + '0' * 64 + '  SHA256SUMS\n']
        for content in attacks:
            with self.subTest(content=content[-90:]):
                (self.root / 'SHA256SUMS').write_text(content)
                with self.assertRaises(ValueError):
                    i.validate(self.root, 'macos', 'x64')
        (self.root / 'SHA256SUMS').write_text(original)
        (self.root / 'extra').write_text('omitted')
        with self.assertRaises(ValueError):
            i.validate(self.root, 'macos', 'x64')

    def test_tamper(self):
        (self.root / 'NOTICE').write_text('tampered')
        with self.assertRaises(ValueError):
            i.validate(self.root, 'macos', 'x64')

    def test_symlink(self):
        (self.root / 'link').symlink_to(self.root / 'NOTICE')
        with self.assertRaises(ValueError):
            i.validate(self.root, 'macos', 'x64')

    def test_wrong_architecture(self):
        with self.assertRaises(ValueError):
            i.validate(self.root, 'macos', 'arm64')

    def test_linux_format_refused(self):
        (self.root / 'host/Lain.Host').write_bytes(b'\x7fELF')
        seal(self.root)
        with self.assertRaises(ValueError):
            i.validate(self.root, 'macos', 'x64')

    def test_secrets_namespace(self):
        for name in ('host/auth.json', 'host/state.db', 'host/.env', 'credentials/token'):
            with self.subTest(name=name):
                p = self.root / name
                p.parent.mkdir(exist_ok=True)
                p.write_text('synthetic')
                seal(self.root)
                with self.assertRaises(ValueError):
                    i.validate(self.root, 'macos', 'x64')
                p.unlink()

    def test_unqualified(self):
        proof = self.root / 'native-qualification.json'
        data = json.loads(proof.read_text())
        data['passed'] = False
        proof.write_text(json.dumps(data))
        paths = [p for p in self.root.rglob('*') if p.is_file() and p.name != 'SHA256SUMS']
        (self.root / 'SHA256SUMS').write_text(''.join(i.digest(p) + '  ' + p.relative_to(self.root).as_posix() + '\n' for p in paths))
        with self.assertRaises(ValueError):
            i.validate(self.root, 'macos', 'x64')

    def test_lifecycle_preserves_data_and_versions(self):
        private = self.base / 'private'
        bindir = self.base / 'bin'
        output = self.base / 'desktop-entry'
        with patch.object(i, 'layout', return_value=(private, bindir)), \
             patch.object(i, 'offline'), patch.object(i, 'architecture', return_value='x64'), \
             patch.object(i, 'integration', side_effect=lambda t, r, a, b: {output: str(a).encode()}), \
             patch.dict(os.environ, {'PATH': str(bindir)}):
            i.mutate('macos', 'install', self.root)
            sentinel = private / 'data/keep.txt'
            sentinel.write_text('synthetic user state')
            i.mutate('macos', 'install', self.root)
            self.assertEqual(len(list((private / 'versions').iterdir())), 1)
            (self.root / 'NOTICE').write_text('new version')
            seal(self.root)
            i.mutate('macos', 'install', self.root)
            self.assertEqual(len(list((private / 'versions').iterdir())), 2)
            i.mutate('macos', 'uninstall')
            self.assertFalse(output.exists())
            self.assertEqual(sentinel.read_text(), 'synthetic user state')
            self.assertEqual(len(list((private / 'versions').iterdir())), 2)

    def test_native_integration_rendering(self):
        import plistlib
        home = self.base / "User With Spaces"
        with patch.object(Path, 'home', return_value=home), patch.dict(os.environ, {'APPDATA': str(home / 'Roaming')}):
            root, app, bindir = home / 'private', home / 'app', home / 'bin'
            files = i.integration('macos', root, app, bindir)
            agent = next(v for p, v in files.items() if 'LaunchAgents' in str(p))
            data = plistlib.loads(agent)
            self.assertEqual(data['ProgramArguments'], [str(app / 'host/Lain.Host')])
            self.assertEqual(data['EnvironmentVariables']['LAIN_HOME'], str(root / 'data'))
            self.assertIn(b'"$@"', files[bindir / 'lain'])
            files = i.integration('windows', root, app, bindir)
            self.assertIn(b'%*', files[bindir / 'lain.cmd'])
            self.assertTrue(any('Startup' in str(p) for p in files))
            self.assertTrue(any(p.name == 'Lain.cmd' for p in files))

    def test_windows_unsafe_home_refused(self):
        home = self.base / 'user%PATH%'
        with patch.object(Path, 'home', return_value=home), patch.dict(os.environ, {'APPDATA': str(home)}):
            with self.assertRaises(ValueError):
                i.integration('windows', home, home / 'app', home / 'bin')

    def test_modified_integration_and_interrupted_install_refused(self):
        private, bindir, entry = self.base / 'private', self.base / 'bin', self.base / 'entry'
        with patch.object(i, 'layout', return_value=(private, bindir)), patch.object(i, 'offline'), \
             patch.object(i, 'architecture', return_value='x64'), \
             patch.object(i, 'integration', return_value={entry: b'ours'}), \
             patch.dict(os.environ, {'PATH': str(bindir)}):
            i.mutate('macos', 'install', self.root)
            entry.write_text('user customized')
            with self.assertRaises(ValueError):
                i.mutate('macos', 'uninstall')
            self.assertEqual(entry.read_text(), 'user customized')
            entry.write_bytes(b'ours')
            (private / 'integration-pending.json').write_text('{}')
            with self.assertRaises(ValueError):
                i.mutate('macos', 'install', self.root)

    def test_foreign_integration_preserved(self):
        private, bindir, entry = self.base / 'private', self.base / 'bin', self.base / 'entry'
        entry.write_text('foreign')
        with patch.object(i, 'layout', return_value=(private, bindir)), patch.object(i, 'offline'), \
             patch.object(i, 'architecture', return_value='x64'), \
             patch.object(i, 'integration', return_value={entry: b'ours'}), \
             patch.dict(os.environ, {'PATH': str(bindir)}):
            with self.assertRaises(ValueError):
                i.mutate('macos', 'install', self.root)
        self.assertEqual(entry.read_text(), 'foreign')


if __name__ == '__main__':
    unittest.main()
