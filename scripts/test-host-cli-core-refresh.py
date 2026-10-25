#!/usr/bin/env python3
"""Synthetic file fixtures only: no payloads, services, DBs, provider calls or builds."""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]


def module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    exec(compile(path.read_bytes(), str(path), 'exec'), result.__dict__)
    return result


api = module(ROOT / 'scripts/host-cli-core-refresh.py', 'bounded_refresh')
common = module(ROOT / 'scripts/core-refresh.py', 'bounded_core_fixture')


class RefreshFixtures(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix='lain-bounded-refresh-')
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.base = self.root / 'baseline'
        self.base.mkdir()
        self.commit = 'a' * 40
        self.version = '1.0.0+' + self.commit
        self.out = self.root / 'staged'
        self.evidence = self.root / 'evidence'
        components = {}
        for name, project in (('cli', 'Cli'), ('host', 'Host'), ('desktop', 'Desktop')):
            folder = self.base / name
            folder.mkdir()
            (folder / ('Lain.' + project)).write_text('nonexecuted synthetic apphost')
            (folder / ('Lain.' + project)).chmod(0o700)
            (folder / ('Lain.' + project + '.dll')).write_bytes(b'old synthetic assembly')
            (folder / 'Lain.Core.dll').write_bytes(b'old synthetic shared assembly')
            (folder / 'runtime.bin').write_bytes(b'unchanged runtime')
            (folder / ('Lain.' + project + '.runtimeconfig.json')).write_text('{"runtimeOptions":{"includedFrameworks":[]}}')
            deps = {'runtimeTarget': {'name': 'net10.0/linux-x64'}, 'libraries': {'External/1': {'type': 'package'}, 'Lain.Core/1': {'type': 'project'}},
                    'targets': {'net10.0/linux-x64': {'External/1': {'runtime': {'runtime.bin': {}}}, 'Lain.Core/1': {}}}}
            common.write(folder / ('Lain.' + project + '.deps.json'), deps)
            proof = self.evidence / name
            proof.mkdir(parents=True)
            (proof / 'packages.lock.json').write_text('{"locked":"unchanged"}')
            (proof / 'project.csproj').write_text('<Project />')
            common.write(proof / 'publish-properties.json', {'sourceCommit': self.commit,
                         'publishProperties': {'InformationalVersion': self.version, 'RuntimeIdentifier': 'linux-x64', 'SelfContained': 'true'}})
            components[name] = {'assemblySha256': api.sha(folder / ('Lain.' + project + '.dll')),
                                'lockfileSha256': api.sha(proof / 'packages.lock.json'), 'sourceCommit': 'b' * 40,
                                'informationalVersion': 'old', 'localSharedAssemblies': {}}
        (self.base / 'proxy').mkdir()
        (self.base / 'proxy/lain-proxy').write_bytes(b'old unexecuted core')
        (self.base / 'notices/go').mkdir(parents=True)
        (self.base / 'notices/go/notice').write_text('old Go notice')
        (self.base / 'notices/app.txt').write_text('retained app notice')
        (self.base / 'packaging').mkdir()
        (self.base / 'packaging/launcher').write_text('retained external integration asset')
        info = {'componentSourceCommits': {'cli': 'b' * 40, 'host': 'b' * 40, 'desktop': 'c' * 40},
                'componentInformationalVersions': {'cli': 'old', 'host': 'old', 'desktop': 'retained'}, 'components': components}
        common.write(self.base / 'build-info.json', info)
        common.write(self.base / 'gate-status.json', {'releaseQualified': True})
        common.manifest(self.base)
        self.before = common.verify(self.base)
        self.core = self.root / 'core-package'
        shutil.copytree(self.base, self.core)
        (self.core / 'proxy/lain-proxy').write_bytes(b'new unexecuted core')
        (self.core / 'notices/go/notice').write_text('new Go notice')
        (self.core / 'core-source-provenance').mkdir()
        (self.core / 'core-source-provenance/evidence').write_text('synthetic qualified source')
        shutil.copyfile(ROOT / 'scripts/core-refresh.py', self.core / 'core-refresh.py')
        common.write(self.core / 'core-refresh.json', {'kind': 'schema2-core-only', 'baseFiles': self.before})
        common.manifest(self.core)
        self.config = {'format': 1, 'kind': 'host-cli-core-refresh-inputs', 'sourceCommit': self.commit,
                       'base': {'path': str(self.base), 'manifestSha256': api.sha(self.base / 'SHA256SUMS')},
                       'corePackage': {'path': str(self.core), 'manifestSha256': api.sha(self.core / 'SHA256SUMS')},
                       'desktopSourceCommit': 'c' * 40, 'helperSha256': api.sha(ROOT / 'scripts/core-refresh.py'), 'output': str(self.out),
                       'sourceEvidence': {'path': str(self.evidence), 'files': common.inventory(self.evidence)}}
        for name, project in (('cli', 'Cli'), ('host', 'Host')):
            folder = self.root / ('fresh-' + name)
            shutil.copytree(self.base / name, folder)
            (folder / ('Lain.' + project + '.dll')).write_bytes(b'new assembly ' + api.version_blob(self.version))
            (folder / 'Lain.Core.dll').write_bytes(b'new shared assembly')
            self.config[name] = {'path': str(folder), 'files': common.inventory(folder), 'informationalVersion': self.version}
        report = {'kind': 'host-cli-core-publish-qualification', 'passed': True, 'sourceCommit': self.commit,
                  'baseManifestSha256': self.config['base']['manifestSha256'], 'coreManifestSha256': self.config['corePackage']['manifestSha256'],
                  'cliFiles': self.config['cli']['files'], 'hostFiles': self.config['host']['files'],
                  'sourceEvidenceFiles': self.config['sourceEvidence']['files'],
                  # Synthetic schema claims only, not a real metadata read.
                  'assemblyMetadata': {name: {'method': 'assembly-metadata-reader',
                      'attribute': 'System.Reflection.AssemblyInformationalVersionAttribute',
                      'dllSha256': self.config[name]['files']['Lain.' + project + '.dll'],
                      'informationalVersion': self.version, 'passed': True}
                      for name, project in (('cli', 'Cli'), ('host', 'Host'))}}
        self.report = self.root / 'qualification.json'
        common.write(self.report, report)
        self.config['qualification'] = {'path': str(self.report), 'sha256': api.sha(self.report)}
        self.inputs = self.root / 'inputs.json'
        self.save_inputs()
        self.args = SimpleNamespace(inputs=self.inputs, inputs_sha256=api.sha(self.inputs), apply=True)

    def save_inputs(self):
        common.write(self.inputs, self.config)

    def stage(self):
        self.args.inputs_sha256 = api.sha(self.inputs)
        # Core qualification itself is separately covered by core-refresh tests.
        # No fake report is presented as a real Core acceptance result.
        with contextlib.redirect_stdout(io.StringIO()), mock.patch.object(api, 'load_helper', return_value=common), mock.patch.object(common, 'check_core', return_value=None):
            api.stage(self.args)

    def install(self, home, apply):
        args = SimpleNamespace(package=self.out, home=home, backup=home / 'backups/update',
                               manifest_sha256=api.sha(self.out / 'SHA256SUMS'), apply=apply, stopped=True)
        with contextlib.redirect_stdout(io.StringIO()), mock.patch.object(api, '__file__', str(self.out / 'host-cli-core-refresh.py')), mock.patch.object(api, 'load_helper', return_value=common), mock.patch.object(common, 'check_core', return_value=None), mock.patch.object(api, 'refuse_running_payloads'):
            api.install(args)

    def installed_fixture(self):
        home = self.root / 'home'
        home.mkdir()
        shutil.copytree(self.base, home / 'app')
        (home / 'app/.lain-package').touch()
        for name in ('configuration.json', 'history.sqlite', 'accounts.sentinel', 'routes-58.sentinel'):
            (home / name).write_bytes(b'private synthetic data must remain unchanged')
        return home

    def test_stage_plan_writes_nothing(self):
        self.args.apply = False
        before = common.inventory(self.root)
        self.stage()
        self.assertEqual(common.inventory(self.root), before)
        self.assertFalse(self.out.exists())

    def test_repeated_delivery_replaces_owned_previous_provenance(self):
        for package in (self.base, self.core):
            previous = package / 'host-cli-core-provenance'
            previous.mkdir()
            (previous / 'old-evidence').write_text('retained in exact baseline input')
        common.manifest(self.base)
        self.before = common.verify(self.base)
        common.write(self.core / 'core-refresh.json', {'kind': 'schema2-core-only', 'baseFiles': self.before})
        common.manifest(self.core)
        self.config['base']['manifestSha256'] = api.sha(self.base / 'SHA256SUMS')
        self.config['corePackage']['manifestSha256'] = api.sha(self.core / 'SHA256SUMS')
        report = common.load(self.report)
        report.update(baseManifestSha256=self.config['base']['manifestSha256'],
                      coreManifestSha256=self.config['corePackage']['manifestSha256'])
        common.write(self.report, report)
        self.config['qualification']['sha256'] = api.sha(self.report)
        self.save_inputs()
        self.stage()
        self.assertFalse((self.out / 'host-cli-core-provenance/old-evidence').exists())
        self.assertTrue((self.out / 'host-cli-core-provenance/inputs.json').is_file())
        self.assertTrue((self.base / 'host-cli-core-provenance/old-evidence').is_file())

    def test_stage_protects_desktop_and_notices(self):
        self.stage()
        target = common.verify(self.out)
        api.protected(self.before, target)
        self.assertEqual(api.subset(target, 'desktop'), api.subset(self.before, 'desktop'))
        self.assertEqual(target['notices/app.txt'], self.before['notices/app.txt'])
        self.assertFalse(common.load(self.out / 'gate-status.json')['releaseQualified'])

    def test_publish_tamper_rejected(self):
        (Path(self.config['host']['path']) / 'Lain.Host.dll').write_bytes(b'tampered')
        with self.assertRaisesRegex(RuntimeError, 'pinned inventory'):
            self.stage()
        self.assertFalse(self.out.exists())

    def test_external_runtime_change_rejected_even_if_repinned(self):
        folder = Path(self.config['host']['path'])
        (folder / 'runtime.bin').write_bytes(b'new dependency needs notices')
        self.config['host']['files'] = common.inventory(folder)
        self.save_inputs()
        with self.assertRaisesRegex(RuntimeError, 'runtime/dependency'):
            self.stage()

    def test_bad_input_pin_rejected_without_output(self):
        self.args.inputs_sha256 = '0' * 64
        with self.assertRaisesRegex(RuntimeError, 'SHA256'):
            api.stage(self.args)
        self.assertFalse(self.out.exists())

    def test_install_plan_writes_nothing(self):
        self.stage()
        home = self.installed_fixture()
        before = common.inventory(home)
        self.install(home, False)
        self.assertEqual(common.inventory(home), before)

    def test_transaction_installs_exact_target_and_retains_private_sentinels(self):
        self.stage()
        home = self.installed_fixture()
        private = {p.name: p.read_bytes() for p in home.iterdir() if p.is_file()}
        self.install(home, True)
        self.assertEqual(common.verify(home / 'app'), common.verify(self.out))
        self.assertEqual(private, {p.name: p.read_bytes() for p in home.iterdir() if p.is_file() and not p.name.startswith('.')})
        journal = common.load(home / 'backups/update/transaction.json')
        self.assertEqual(journal['status'], 'installed-not-started')

    def test_changed_installed_baseline_rejected(self):
        self.stage()
        home = self.installed_fixture()
        (home / 'app/desktop/runtime.bin').write_text('foreign modification')
        with self.assertRaisesRegex(RuntimeError, 'inventory differs'):
            self.install(home, True)
        self.assertFalse((home / 'backups').exists())

    def test_protected_desktop_change_rejected_even_if_resealed(self):
        self.stage()
        (self.out / 'desktop/runtime.bin').write_text('changed desktop')
        common.manifest(self.out)
        home = self.installed_fixture()
        with self.assertRaisesRegex(RuntimeError, 'protected'):
            self.install(home, True)

    def test_interrupted_file_transaction_reverses_completed_exchanges(self):
        self.stage()
        home = self.installed_fixture()
        write = common.write
        interrupted = False

        def fail_before_host(path, value):
            nonlocal interrupted
            if value.get('status') == 'replacing' and value.get('activeItem', [None])[0] == 'host' and not interrupted:
                interrupted = True
                raise OSError('synthetic interrupted journal write')
            return write(path, value)

        with mock.patch.object(common, 'write', side_effect=fail_before_host):
            with self.assertRaisesRegex(OSError, 'synthetic interrupted'):
                self.install(home, True)
        self.assertEqual(common.verify(home / 'app'), self.before)
        self.assertEqual(common.load(home / 'backups/update/transaction.json')['status'], 'file-transaction-reversed')

    def test_apply_refuses_symlink_lock_without_touching_target(self):
        self.stage()
        home = self.installed_fixture()
        target = self.root / 'foreign-lock-target'
        target.write_bytes(b'foreign target must stay byte-identical')
        target.chmod(0o600)
        before = target.stat()
        (home / '.host-cli-core-refresh.lock').symlink_to(target)
        with self.assertRaises(OSError):
            self.install(home, True)
        self.assertEqual(target.read_bytes(), b'foreign target must stay byte-identical')
        self.assertEqual(target.stat().st_mtime_ns, before.st_mtime_ns)
        self.assertEqual(common.verify(home / 'app'), self.before)
        self.assertFalse((home / 'backups').exists())

    def test_lock_refuses_hardlink_and_open_permissions(self):
        home = self.root / 'lock-home'
        home.mkdir(mode=0o700)
        lock = home / '.host-cli-core-refresh.lock'
        original = self.root / 'hardlinked-file'
        original.write_bytes(b'no writes')
        original.chmod(0o600)
        os.link(original, lock)
        with self.assertRaisesRegex(RuntimeError, 'one link'):
            with api.updater_lock(home):
                self.fail('unsafe hardlink lock accepted')
        lock.unlink()
        lock.write_bytes(b'no writes')
        lock.chmod(0o644)
        with self.assertRaisesRegex(RuntimeError, 'private regular'):
            with api.updater_lock(home):
                self.fail('unsafe permissions accepted')
        self.assertEqual(original.read_bytes(), b'no writes')
        self.assertEqual(lock.read_bytes(), b'no writes')

    def test_lock_reuses_owned_private_file_and_refuses_contender(self):
        home = self.root / 'lock-home'
        home.mkdir(mode=0o700)
        with api.updater_lock(home):
            with self.assertRaises(BlockingIOError):
                with api.updater_lock(home):
                    self.fail('concurrent lock accepted')
        with api.updater_lock(home):
            self.assertEqual((home / '.host-cli-core-refresh.lock').stat().st_mode & 0o777, 0o600)

    def test_new_backup_ancestor_entries_synced_before_first_exchange(self):
        self.stage()
        home = self.installed_fixture()
        events = []
        sync = common.sync_directory
        replace = api.replace_items

        def sync_record(path):
            events.append(('sync', Path(path)))
            return sync(path)

        def replace_record(*args):
            events.append(('exchange', None))
            return replace(*args)

        with mock.patch.object(common, 'sync_directory', side_effect=sync_record), mock.patch.object(api, 'replace_items', side_effect=replace_record):
            self.install(home, True)
        before = events[:events.index(('exchange', None))]
        for directory in (home, home / 'backups', home / 'backups/update'):
            self.assertIn(('sync', directory), before)

    def test_report_requires_exact_dll_metadata_read_claims(self):
        original = common.load(self.report)
        mutations = [
            lambda value: value.pop('assemblyMetadata'),
            lambda value: value['assemblyMetadata']['cli'].update(method='byte-marker-only'),
            lambda value: value['assemblyMetadata']['host'].update(dllSha256='0' * 64),
            lambda value: value['assemblyMetadata']['host'].update(informationalVersion='old-version'),
        ]
        for mutate in mutations:
            value = json.loads(json.dumps(original))
            mutate(value)
            common.write(self.report, value)
            self.config['qualification']['sha256'] = api.sha(self.report)
            self.save_inputs()
            with self.assertRaisesRegex(RuntimeError, 'actual metadata read'):
                self.stage()
            self.assertFalse(self.out.exists())


if __name__ == '__main__':
    unittest.main()
