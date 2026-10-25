#!/usr/bin/env python3
"""Temporary-file fixtures; no payload, service, build or network execution."""
import contextlib
import copy
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
            'history': {'from': '2026-10-01', 'to': '2020-01-01'}}
        for path in self.config['installation']['externalFiles']:
            path = Path(path)
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('synthetic nonexecuted integration')
        self.config['installation']['launcherSha256'] = api.sha(Path(self.config['installation']['launcher']))
        self.config['installation']['unitSha256'] = api.sha(Path(self.config['installation']['externalFiles'][1]))
        report = {key: self.config[key] for key in ('sourceCommit', 'desktopSourceCommit', 'installerSha256',
                  'coreHelperSha256', 'core', 'preservationBaseline', 'operatorSha256', 'operatorHelpers', 'history')}
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

    def plan(self, **kwargs):
        def load(path, expected, name):
            operator.pinned(path, expected)
            return api if path.name == 'host-cli-core-refresh.py' else common
        with patch.object(operator, 'module', side_effect=load), patch.object(common, 'check_core'), \
             patch('subprocess.run', side_effect=AssertionError('No subprocess in plan')):
            return operator.plan(self.config, **kwargs)

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

    def test_postflight_explicitly_requires_target_and_preflight_stays_strict(self):
        shutil.rmtree(Path(self.config['installation']['home']) / 'app')
        shutil.copytree(self.f.out, Path(self.config['installation']['home']) / 'app')
        with self.assertRaisesRegex(RuntimeError, 'baseline'):
            self.plan()
        self.plan(installed='target', fresh=False)

    def test_inherited_matching_home_refused_before_any_subprocess(self):
        with patch.dict('os.environ', {'LAIN_HOME': self.config['installation']['home']}):
            with self.assertRaisesRegex(RuntimeError, 'env -u LAIN_HOME'):
                self.plan()

    def test_missing_app_backup_parent_refused_in_plan(self):
        self.config['appDeltaBackup'] = str(Path(self.config['installation']['home']) / 'missing-parent/delta')
        with self.assertRaisesRegex(RuntimeError, 'Fresh app delta backup'):
            self.plan()
        self.assertFalse(Path(self.config['backup']).exists())

    def test_unqualified_report_refused_before_mutation(self):
        path = Path(self.config['qualification']['path'])
        report = common.load(path)
        report['checks']['quietRequestDrain']['passed'] = False
        common.write(path, report)
        self.config['qualification']['sha256'] = api.sha(path)
        with self.assertRaisesRegex(RuntimeError, 'quietRequestDrain'):
            self.plan()
        self.assertFalse(Path(self.config['backup']).exists())

    def full_package(self):
        self.config.update(packageVariant='full-components', desktopSourceCommit=self.f.commit,
                           installerPath=str(HERE / 'package-refresh.py'),
                           installerSha256=api.sha(HERE / 'package-refresh.py'),
                           coreHelperPath=str(HERE / 'core-refresh.py'))
        (self.f.out / 'desktop/Lain.Desktop.dll').write_bytes(b'new synthetic Desktop')
        info = common.load(self.f.out / 'build-info.json')
        info['coreSourceCommit'] = self.f.commit
        self.config['core']['sourceCommit'] = self.f.commit
        info['componentSourceCommits']['desktop'] = self.f.commit
        info['componentInformationalVersions']['desktop'] = '1.0.0+' + self.f.commit
        info['components']['desktop']['assemblySha256'] = api.sha(self.f.out / 'desktop/Lain.Desktop.dll')
        info.update(commit=self.f.commit, compileInputsFrozen=True, runtime='linux-x64', runtimeMode='gateway')
        records = []
        for name, project in (('cli', 'Lain.Cli'), ('host', 'Lain.Host'), ('desktop', 'Lain.Desktop')):
            item = info['components'][name]
            item.update(sourceCommit=self.f.commit, informationalVersion='1.0.0+' + self.f.commit,
                        localSharedAssemblies={'Lain.Core': api.sha(self.f.out / name / 'Lain.Core.dll')})
            properties = self.f.out / 'source-provenance/publish-properties' / (name + '.json')
            properties.parent.mkdir(parents=True, exist_ok=True)
            common.write(properties, {'sourceCommit': self.f.commit, 'project': project, 'publishProperties': {
                         'InformationalVersion': '1.0.0+' + self.f.commit, 'RuntimeIdentifier': 'linux-x64', 'SelfContained': 'true'}})
            lock = self.f.out / 'source-provenance/source/src' / project / 'packages.lock.json'
            lock.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(self.f.evidence / name / 'packages.lock.json', lock)
            for path in (properties, lock):
                records.append({'path': str(path.relative_to(self.f.out)), 'sha256': api.sha(path)})
        common.write(self.f.out / 'source-provenance/inventory.json', {'format': 1, 'sourceCommit': self.f.commit, 'files': records})
        info['sourceProvenanceInventorySha256'] = api.sha(self.f.out / 'source-provenance/inventory.json')
        common.write(self.f.out / 'proxy/source-manifest.json', {'buildInputs': {}})
        path = self.f.out / 'core-source-provenance/proxy/source-manifest.json'
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(self.f.out / 'proxy/source-manifest.json', path)
        self.config['componentInformationalVersions'] = info['componentInformationalVersions']
        common.write(self.f.out / 'build-info.json', info)
        common.manifest(self.f.out)
        self.config['package']['manifestSha256'] = api.sha(self.f.out / 'SHA256SUMS')
        path = Path(self.config['qualification']['path'])
        report = common.load(path)
        report.update(kind='portable-full-service-update-qualification', packageVariant='full-components',
                      desktopSourceCommit=self.f.commit, componentInformationalVersions=self.config['componentInformationalVersions'],
                      core=self.config['core'], installerSha256=self.config['installerSha256'],
                      packageManifestSha256=self.config['package']['manifestSha256'])
        for key in ('mixedSourceProvenance', 'retainedDesktopBytes'): report['checks'].pop(key)
        for key in ('fullComponentSourceProvenance', 'desktopReplacement'):
            report['checks'][key] = {'passed': True, 'evidence': 'synthetic-schema-only'}
        common.write(path, report)
        self.config['qualification']['sha256'] = api.sha(path)

    def test_full_desktop_and_host_cli_replacement_success(self):
        self.full_package()
        update, state = self.execute_fixture()
        self.assertEqual(update.record['phase'], 'verified')
        self.assertEqual(state['services'], ['stop', 'start'])
        self.assertEqual((Path(self.config['installation']['home']) / 'app/desktop/Lain.Desktop.dll').read_bytes(),
                         b'new synthetic Desktop')

    def test_full_publish_sdk_extended_versions_are_explicit_and_bound(self):
        self.full_package()
        info = common.load(self.f.out / 'build-info.json')
        inventory = common.load(self.f.out / 'source-provenance/inventory.json')
        version = '1.0.0+' + self.f.commit[:7] + '.' + self.f.commit
        for name in ('cli', 'host', 'desktop'):
            info['componentInformationalVersions'][name] = version
            info['components'][name]['informationalVersion'] = version
            path = self.f.out / 'source-provenance/publish-properties' / (name + '.json')
            values = common.load(path)
            values['publishProperties']['InformationalVersion'] = version
            common.write(path, values)
            for item in inventory['files']:
                if item['path'] == str(path.relative_to(self.f.out)): item['sha256'] = api.sha(path)
        common.write(self.f.out / 'source-provenance/inventory.json', inventory)
        info['sourceProvenanceInventorySha256'] = api.sha(self.f.out / 'source-provenance/inventory.json')
        common.write(self.f.out / 'build-info.json', info)
        self.reseal_full_report(info)
        self.plan()

    def reseal_full_report(self, info):
        common.manifest(self.f.out)
        self.config['componentInformationalVersions'] = info['componentInformationalVersions']
        self.config['package']['manifestSha256'] = api.sha(self.f.out / 'SHA256SUMS')
        path = Path(self.config['qualification']['path'])
        report = common.load(path)
        report.update(componentInformationalVersions=self.config['componentInformationalVersions'],
                      packageManifestSha256=self.config['package']['manifestSha256'])
        common.write(path, report)
        self.config['qualification']['sha256'] = api.sha(path)

    def test_resealed_full_component_metadata_contradiction_refused(self):
        self.full_package()
        info = common.load(self.f.out / 'build-info.json')
        info['components']['desktop']['localSharedAssemblies'] = {}
        common.write(self.f.out / 'build-info.json', info)
        self.reseal_full_report(info)
        with self.assertRaisesRegex(RuntimeError, 'shared assembly provenance differs'):
            self.plan()
        self.assertFalse(Path(self.config['backup']).exists())

    def test_full_package_cannot_use_retained_desktop_qualification(self):
        self.full_package()
        path = Path(self.config['qualification']['path'])
        report = common.load(path)
        report['kind'] = 'portable-mixed-service-update-qualification'
        common.write(path, report)
        self.config['qualification']['sha256'] = api.sha(path)
        with self.assertRaisesRegex(RuntimeError, 'qualification missing'):
            self.plan()

    def test_pinned_package_helper_import_does_not_write_bytecode(self):
        package = module('package-refresh')
        helper = self.f.root / 'isolated-core-refresh.py'
        shutil.copyfile(HERE / 'core-refresh.py', helper)
        before = common.inventory(self.f.root)
        package.load_helper(helper, api.sha(helper))
        self.assertEqual(common.inventory(self.f.root), before)
        self.assertFalse((self.f.root / '__pycache__').exists())

    def full_installer_args(self):
        home = Path(self.config['installation']['home'])
        return SimpleNamespace(package=self.f.out, home=home, backup=home / 'full-delta',
            manifest_sha256=self.config['package']['manifestSha256'],
            baseline_manifest_sha256=self.config['baseline']['manifestSha256'],
            helper=HERE / 'core-refresh.py', helper_sha256=api.sha(HERE / 'core-refresh.py'), apply=True, stopped=True)

    def test_full_exchange_follows_payload_sync_and_durable_prepared_journal(self):
        self.full_package()
        package = module('package-refresh')
        args = self.full_installer_args()
        (args.home / 'backups').mkdir(mode=0o700)
        args.backup = args.home / 'backups/nested-delta'
        synced = set()
        original_sync, original_exchange = package.os.fsync, package.exchange
        def fsync(fd):
            synced.add(package.os.readlink('/proc/self/fd/' + str(fd)))
            return original_sync(fd)
        def exchange(left, right):
            journal = common.load(args.backup / 'transaction.json')
            self.assertEqual(journal['status'], 'prepared')
            self.assertTrue({str(path) for path in right.rglob('*') if path.is_file()} <= synced)
            self.assertIn(str(right), synced)
            self.assertIn(str(right.parent), synced)
            self.assertIn(str(args.backup), synced)
            self.assertIn(str(args.backup.parent), synced)
            self.assertIn(str(args.home), synced)
            original_exchange(left, right)
        with patch.object(package, 'no_payloads'), patch.object(package.os, 'fsync', side_effect=fsync), \
             patch.object(package, 'exchange', side_effect=exchange), contextlib.redirect_stdout(io.StringIO()):
            package.install(args)
        self.assertEqual(common.load(args.backup / 'transaction.json')['status'], 'installed')

    def test_lost_exchange_completion_retains_old_app_and_prepared_journal(self):
        self.full_package()
        package = module('package-refresh')
        args = self.full_installer_args()
        before = common.verify(args.home / 'app')
        actual_exchange = package.exchange
        def lost_completion(left, right):
            actual_exchange(left, right)
            raise OSError('synthetic lost exchange completion')
        with patch.object(package, 'no_payloads'), patch.object(package, 'exchange', side_effect=lost_completion), \
             contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaisesRegex(RuntimeError, 'lost exchange completion'):
                package.install(args)
        journal = common.load(args.backup / 'transaction.json')
        self.assertEqual(journal['status'], 'prepared')
        self.assertEqual(common.verify(Path(journal['preparedApp'])), before)
        self.assertEqual(common.verify(args.home / 'app'), common.verify(self.f.out))
        self.assertEqual((args.home / 'configuration.json').read_bytes(), b'private synthetic data must remain unchanged')

    def test_full_app_transaction_writefault_reverses_only_app(self):
        self.full_package()
        package = module('package-refresh')
        home = Path(self.config['installation']['home'])
        before = common.verify(home / 'app')
        original_rename = Path.rename
        failed = {'once': False}
        def rename(path, target):
            if path.name == 'app' and path.parent.name.startswith('.package-refresh-') and not failed['once']:
                failed['once'] = True
                raise OSError('synthetic full-app writefault')
            return original_rename(path, target)
        args = SimpleNamespace(package=self.f.out, home=home, backup=home / 'full-delta',
            manifest_sha256=self.config['package']['manifestSha256'],
            baseline_manifest_sha256=self.config['baseline']['manifestSha256'],
            helper=HERE / 'core-refresh.py', helper_sha256=api.sha(HERE / 'core-refresh.py'), apply=True, stopped=True)
        with patch.object(package, 'no_payloads'), patch.object(Path, 'rename', rename), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaisesRegex(OSError, 'full-app writefault'):
                package.install(args)
        self.assertEqual(common.verify(home / 'app'), before)
        self.assertEqual((home / 'configuration.json').read_bytes(), b'private synthetic data must remain unchanged')

    def execute_fixture(self, postflight_failure=False, stuck_new_stream=False, append_new_usage=False):
        home = Path(self.config['installation']['home'])
        primitive = module('service-update-primitives')
        for name in primitive.FROZEN:
            path = home / name
            if not path.exists():
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b'synthetic frozen state')
        expected, _, before_files, after_files = self.plan()
        args = SimpleNamespace(backup=Path(self.config['backup']), previous_source_commit='old',
                               new_source_commit=self.f.commit, history_from='2019-01-01', history_to='2020-01-01')
        cls, old = operator.prepare_update_class(args, self.config, expected, common, before_files, after_files)
        update = cls()
        update.guard = lambda: None
        update.watch = lambda: None
        update.own_unit = lambda **kwargs: {'MainPID': '100'}
        old.no_writers = lambda: None
        old.no_desktop = lambda: None
        state = {'started': False, 'draining': False, 'active': 0, 'services': [], 'poststartActivity': 0}
        totals = {'requests': 1, 'attempts': 1, 'errors': 0, 'unknownUsageAttempts': 0,
                  'tokens': {'input': 2, 'output': 3, 'cached': 0, 'reasoning': 0, 'total': 5}}
        capture = {'status': {'version': 'old', 'runtimeMode': 'gateway', 'proxyReady': True,
                             'revision': 9, 'appliedRevision': 8},
                   'accounts': [{'id': 'one'}, {'id': 'two'}], 'profiles': [{'id': 'profile'}],
                   'models': {'models': []}, 'history': {'cursor': 1, 'totals': totals}}
        def captured(name):
            value = copy.deepcopy(capture)
            if state['started']:
                value['status']['version'] = '1.0.0+' + self.f.commit
                if append_new_usage: value['history']['cursor'] = 2
            return value
        update.capture = captured
        update.cli = lambda args: captured('status')['status']
        update.backend_identity = lambda files=before_files, **kwargs: {'host': files['host/Lain.Host.dll']}
        update.management_token = lambda: 'synthetic-management-token'
        def activity(token, method, route, timeout):
            if method == 'POST': state['draining'] = True
            elif method == 'DELETE': state['draining'] = False
            if state['started'] and route == '/lain/activity':
                state['poststartActivity'] += 1
                if not stuck_new_stream and state['draining'] and state['poststartActivity'] >= 3:
                    state['active'] = 0
            return {'draining': state['draining'], 'activeRequests': state['active']}
        update.activity_request = activity
        def service(action, **kwargs):
            if action == 'stop':
                self.assertEqual(state['active'], 0, 'No stop may interrupt an admitted stream')
            state['services'].append(action)
            if action == 'start':
                state.update(started=True, draining=False, active=1 if append_new_usage or stuck_new_stream else 0)
                if append_new_usage:
                    with (home / 'proxy/events.jsonl').open('ab') as stream: stream.write(b'\nsynthetic appended usage')
        update.service = service
        maintenance = module('lain-maintenance')
        def run(command, **kwargs):
            if command[2] == 'backup':
                with patch.object(maintenance, 'stopped'):
                    maintenance.backup(Path(command[3]), Path(command[4]))
            elif self.config.get('packageVariant') == 'full-components':
                package = module('package-refresh')
                with patch.object(package, 'no_payloads'):
                    package.install(SimpleNamespace(package=self.f.out, home=home,
                        backup=Path(self.config['appDeltaBackup']), manifest_sha256=self.config['package']['manifestSha256'],
                        baseline_manifest_sha256=self.config['baseline']['manifestSha256'],
                        helper=HERE / 'core-refresh.py', helper_sha256=api.sha(HERE / 'core-refresh.py'), apply=True, stopped=True))
            else:
                with patch.object(api, '__file__', str(self.f.out / 'host-cli-core-refresh.py')), \
                     patch.object(api, 'load_helper', return_value=common), patch.object(api, 'refuse_running_payloads'):
                    api.install(SimpleNamespace(package=self.f.out, home=home,
                                backup=Path(self.config['appDeltaBackup']),
                                manifest_sha256=self.config['package']['manifestSha256'], stopped=True, apply=True))
            return b''
        update.run = run
        actual_plan = operator.plan
        actual_module = operator.module
        def load(path, expected, name):
            if path.name == 'core-refresh.py': return common
            if path.name == 'host-cli-core-refresh.py': return api
            return actual_module(path, expected, name)
        def checked_plan(config, **kwargs):
            if postflight_failure and kwargs.get('installed') == 'target':
                raise RuntimeError('synthetic precise postflight failure')
            return actual_plan(config, **kwargs)
        clock = {'value': 0}
        def monotonic():
            clock['value'] += 21 if state['started'] and stuck_new_stream else .01
            return clock['value']
        with patch.object(operator, 'module', side_effect=load), patch.object(operator, 'plan', side_effect=checked_plan), \
             patch.object(common, 'check_core'), patch.object(operator.time, 'sleep'), \
             patch.object(operator.time, 'monotonic', side_effect=monotonic), contextlib.redirect_stdout(io.StringIO()):
            if postflight_failure or stuck_new_stream:
                with self.assertRaises(RuntimeError): update.execute()
            else:
                update.execute()
        return update, state

    def test_actual_file_stage_install_start_postflight_success(self):
        update, state = self.execute_fixture(append_new_usage=True)
        self.assertEqual(update.record['phase'], 'verified')
        self.assertEqual(state['services'], ['stop', 'start'])
        self.assertFalse(state['draining'])
        self.assertGreaterEqual(state['poststartActivity'], 3)

    def test_postflight_failure_after_admitted_request_stops_only_quiet_new_backend(self):
        update, state = self.execute_fixture(postflight_failure=True, append_new_usage=True)
        self.assertEqual(update.record['errorMessage'], 'synthetic precise postflight failure')
        self.assertEqual(state['services'], ['stop', 'start', 'stop'])
        self.assertEqual(state['active'], 0)

    def test_new_active_stream_timeout_never_stops_running_backend(self):
        update, state = self.execute_fixture(stuck_new_stream=True)
        self.assertEqual(state['services'], ['stop', 'start'])
        self.assertEqual(state['active'], 1)
        self.assertEqual(update.record['phase'], 'failed-poststart-recovery-required')
        self.assertTrue(update.record['admissionRestored'])

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
        isolated = self.f.root / 'isolated-operator-source'
        isolated.mkdir()
        for name in ('service-update.py', 'lain-maintenance.py'):
            shutil.copyfile(HERE / name, isolated / name)
        recovery.HERE = isolated
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
