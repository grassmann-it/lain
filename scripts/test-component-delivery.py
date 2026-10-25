#!/usr/bin/env python3
"""Temporary-file fixtures; no payload, service, build or network execution."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import shutil
from types import SimpleNamespace
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent


def module(name):
    spec = importlib.util.spec_from_file_location(name.replace('-', '_'), HERE / (name + '.py'))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


fixture_module = module('test-host-cli-core-refresh')
api, common = fixture_module.api, fixture_module.common
operator = module('service-update')


class PortablePlanFixtures(unittest.TestCase):
    def setUp(self):
        fixture = fixture_module.RefreshFixtures('test_stage_plan_writes_nothing')
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        self.f = fixture
        common.write(fixture.core / 'core-source-provenance/inventory.json', {'sourceCommit': 'd' * 40})
        common.manifest(fixture.core)
        fixture.config['corePackage']['manifestSha256'] = api.sha(fixture.core / 'SHA256SUMS')
        report = common.load(fixture.report)
        report['coreManifestSha256'] = fixture.config['corePackage']['manifestSha256']
        common.write(fixture.report, report)
        fixture.config['qualification']['sha256'] = api.sha(fixture.report)
        fixture.save_inputs()
        fixture.stage()
        home = fixture.installed_fixture()
        self.config = {
            'format': 1, 'kind': 'portable-mixed-service-update-inputs', 'sourceCommit': fixture.commit,
            'desktopSourceCommit': 'c' * 40,
            'installation': {'home': str(home), 'prefix': str(fixture.root / 'prefix'),
                'configHome': str(fixture.root / 'config'), 'codexHome': str(fixture.root / 'codex'),
                'launcher': str(fixture.root / 'prefix/bin/lain'), 'managementPort': 18417,
                'externalFiles': [str(fixture.root / 'prefix/bin/lain'), str(fixture.root / 'config/systemd/user/lain.service')]},
            'operatorSha256': api.sha(HERE / 'service-update.py'),
            'operatorHelpers': {'primitivesSha256': api.sha(HERE / 'service-update-primitives.py'),
                                'platformSha256': api.sha(HERE / 'service-update-platform.py')},
            'package': {'path': str(fixture.out), 'manifestSha256': api.sha(fixture.out / 'SHA256SUMS')},
            'baseline': {'path': str(fixture.base), 'manifestSha256': api.sha(fixture.base / 'SHA256SUMS')},
            'installerSha256': api.sha(fixture.out / 'host-cli-core-refresh.py'),
            'coreHelperSha256': api.sha(fixture.out / 'core-refresh.py'), 'previousHostVersion': 'old',
            'core': {'sha256': api.sha(fixture.out / 'proxy/lain-proxy'), 'sourceCommit': 'd' * 40},
            'backup': str(fixture.root / 'full-backup'), 'appDeltaBackup': str(home / 'delta-backup'),
            'preservationBaseline': {'accounts': 2, 'routes': 7, 'profiles': 1, 'revision': 9, 'appliedRevision': 8,
                                     'configurationSha256': api.sha(home / 'configuration.json')},
            'legacyFingerprints': [], 'mainCodexFingerprints': [
                {'path': str(fixture.root / 'codex' / name), 'missing': True} for name in ('config.toml', 'auth.json')],
            'history': {'from': '2026-10-01', 'to': '2026-10-26'}}
        for path in self.config['installation']['externalFiles']:
            path = Path(path)
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('synthetic nonexecuted integration')
        self.config['installation']['launcherSha256'] = api.sha(Path(self.config['installation']['launcher']))
        self.config['installation']['unitSha256'] = api.sha(Path(self.config['installation']['externalFiles'][1]))
        report = {key: self.config[key] for key in ('sourceCommit', 'desktopSourceCommit', 'installerSha256',
                  'coreHelperSha256', 'core', 'preservationBaseline', 'operatorSha256', 'operatorHelpers')}
        report.update(kind='portable-mixed-service-update-qualification', format=1, status='passed', planOnly=False,
                      syntheticOnly=True, baselineManifestSha256=self.config['baseline']['manifestSha256'],
                      packageManifestSha256=self.config['package']['manifestSha256'])
        report['checks'] = {name: {'passed': True, 'evidence': 'synthetic-schema-fixture-only'} for name in (
            'exactArtifactTests', 'mixedSourceProvenance', 'retainedDesktopBytes', 'serviceFreeInstaller',
            'appProcessRefusal', 'fileTransactionFailureRecovery', 'stoppedBackup', 'syntheticStatePreservation',
            'quietRequestDrain', 'ownServiceIdentity', 'mainCodexPreservation')}
        path = fixture.root / 'mixed-qualification.json'
        common.write(path, report)
        self.config['qualification'] = {'path': str(path), 'sha256': api.sha(path)}

    def plan(self):
        def load(path, expected, name):
            operator.pinned(path, expected)
            return api if path.name == 'host-cli-core-refresh.py' else common
        with patch.object(operator, 'module', side_effect=load), patch.object(common, 'check_core'), \
             patch('subprocess.run', side_effect=AssertionError('No subprocess in plan')):
            return operator.plan(self.config)

    def test_plan_accepts_explicit_nonhistorical_counts_and_writes_nothing(self):
        before = common.inventory(self.f.root)
        result = self.plan()[0]
        self.assertEqual(result['preservationBaseline']['accounts'], 2)
        self.assertEqual(common.inventory(self.f.root), before)

    def test_wrong_installed_baseline_refused_before_any_subprocess(self):
        (Path(self.config['installation']['home']) / 'app/host/Lain.Host.dll').write_bytes(b'wrong predecessor')
        with self.assertRaisesRegex(RuntimeError, 'inventory differs|baseline'):
            self.plan()
        self.assertFalse(Path(self.config['backup']).exists())

    def test_package_tamper_refused_before_any_subprocess(self):
        (self.f.out / 'host/Lain.Host.dll').write_bytes(b'tamper')
        with self.assertRaisesRegex(RuntimeError, 'inventory differs'):
            self.plan()

    def test_evidence_tamper_refused(self):
        Path(self.config['qualification']['path']).write_text('{}')
        with self.assertRaisesRegex(RuntimeError, 'pin differs'):
            self.plan()

    def test_failed_command_retains_private_original_stderr(self):
        primitives = module('service-update-primitives')
        update = object.__new__(primitives.Update)
        update.backup, update.env = self.f.root, {}
        update.guard = lambda: None
        result = SimpleNamespace(returncode=27, stdout=b'synthetic output', stderr=b'specific original installer failure')
        with patch('subprocess.run', return_value=result):
            with self.assertRaisesRegex(RuntimeError, 'exit 27'):
                update.run(['/usr/bin/python3', 'synthetic-helper'])
        self.assertIn('specific original installer failure', (self.f.root / 'private-errors.log').read_text())

    def test_identity_failure_diagnostic_contains_only_changed_key_hashes(self):
        with self.assertRaisesRegex(RuntimeError, 'identity changed'):
            operator.require_identity({'accounts': ['synthetic-private-before'], 'revision': 3},
                                      {'accounts': ['synthetic-private-after'], 'revision': 3},
                                      self.f.root, 'identity changed')
        raw = (self.f.root / 'protected-state-delta.json').read_text()
        self.assertNotIn('synthetic-private', raw)
        self.assertEqual(set(json.loads(raw)['changed']), {'accounts'})

    def test_publish_binding_plan_then_explicit_write(self):
        binder = module('prepare-component-delivery')
        delivery = self.f.root / 'delivery'
        delivery.mkdir()
        for name in ('cli', 'host'):
            shutil.copytree(Path(self.f.config[name]['path']), delivery / name)
        shutil.copytree(self.f.evidence, delivery / 'source-evidence')
        metadata = self.f.root / 'metadata.json'
        common.write(metadata, common.load(self.f.report)['assemblyMetadata'])
        out = self.f.root / 'bind-output'
        args = ['prepare-component-delivery.py', '--delivery', str(delivery), '--stage-dir', str(out),
                '--core-package', str(self.f.core), '--baseline', str(self.f.base),
                '--metadata', str(metadata), '--metadata-sha256', api.sha(metadata),
                '--core-manifest-sha256', api.sha(self.f.core / 'SHA256SUMS'),
                '--baseline-manifest-sha256', api.sha(self.f.base / 'SHA256SUMS'),
                '--source-commit', self.f.commit, '--desktop-source-commit', 'c' * 40,
                '--installer-sha256', api.sha(HERE / 'host-cli-core-refresh.py'),
                '--helper-sha256', api.sha(HERE / 'core-refresh.py')]
        before = common.inventory(self.f.root)
        with patch.object(binder, 'load_api', return_value=(api, common)), patch.object(common, 'check_core'), \
             patch('sys.argv', args), contextlib.redirect_stdout(io.StringIO()):
            binder.main()
        self.assertEqual(common.inventory(self.f.root), before)
        with patch.object(binder, 'load_api', return_value=(api, common)), patch.object(common, 'check_core'), \
             patch('sys.argv', args + ['--apply']), contextlib.redirect_stdout(io.StringIO()):
            binder.main()
        self.assertTrue((out / 'mixed-inputs.json').is_file())
        self.assertFalse((out / 'package').exists())
        self.assertEqual(common.load(out / 'mixed-inputs.json')['desktopSourceCommit'], 'c' * 40)

    def test_recovery_plan_and_quarantine(self):
        maintenance = module('lain-maintenance')
        recovery = module('service-recovery')
        source = self.f.root / 'synthetic-state'
        source.mkdir()
        (source / 'credential.sentinel').write_bytes(b'synthetic-current-generation')
        snapshot, destination = self.f.root / 'snapshot', self.f.root / 'recovery'
        with patch.object(maintenance, 'stopped'):
            maintenance.backup(source, snapshot)
        args = ['service-recovery.py', '--snapshot', str(snapshot), '--snapshot-manifest-sha256',
                api.sha(snapshot / 'snapshot.json'), '--helper-sha256', api.sha(HERE / 'lain-maintenance.py'),
                '--destination', str(destination)]
        before = common.inventory(self.f.root)
        with patch('sys.argv', args), contextlib.redirect_stdout(io.StringIO()):
            recovery.main()
        self.assertEqual(common.inventory(self.f.root), before)
        with patch('sys.argv', args + ['--execute', '--stopped']), contextlib.redirect_stdout(io.StringIO()), \
             patch.object(maintenance, 'stopped'), patch.object(operator, 'module', return_value=maintenance):
            # Restore's process check is read-only; the real helper writes only this fresh temp home.
            recovery.main()
        self.assertEqual((destination / 'credential.sentinel').read_bytes(), b'synthetic-current-generation')
        self.assertTrue((destination / 'restore-requires-reconnect').is_file())
        self.assertTrue(source.is_dir())


if __name__ == '__main__':
    unittest.main()
