"""Synthetic upgrade state-machine tests. No real systemd/API, SQLite, or build."""
import argparse
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import upgrade_probe as probe


class UpgradeProbeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='lain-upgrade-fixture-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.state = self.root / 'owned'
        self.package = self.root / 'package with spaces'
        self.service_active = True
        self.transitions = []
        self.config = {'revision': 4, 'appliedRevision': 4, 'accounts': [], 'routes': [], 'profiles': []}

        def write(path, content, executable=False):
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content)
            path.chmod(0o700 if executable else 0o600)

        self.write = write
        self.state.mkdir(mode=0o700)
        for directory, executable in [('host', 'Lain.Host'), ('cli', 'Lain.Cli'),
                                      ('desktop', 'Lain.Desktop'), ('proxy', 'lain-proxy')]:
            write(self.package / directory / executable, 'new-' + directory, True)
            write(self.state / 'app' / directory / executable, 'old-' + directory, True)
        write(self.state / 'app/.lain-package', '')
        write(self.state / 'configuration.json', json.dumps(self.config))
        write(self.state / 'active-profile.json', '{"id":"fixture","revision":4}')
        write(self.state / 'credentials/host.token', 'synthetic-local-token')
        write(self.state / 'credentials/account.json', '{"apiKey":"synthetic-only"}')
        write(self.state / 'proxy/events.jsonl', 'old durable event\n')
        write(self.state / 'proxy/events.jsonl.source', 'stable-source')
        write(self.state / 'proxy/config.yaml', 'synthetic-runtime')
        write(self.state / 'proxy/bindings.json', '{}')
        write(self.state / 'proxy/runtime.json', '{"valid":true,"revision":4}')
        write(self.state / 'proxy/auth-owned/account.json', '{"access":"synthetic-snapshot"}')
        write(self.state / 'usage.db', 'synthetic-old-schema-bytes')
        legacy = self.root / 'legacy'
        write(legacy / 'cpa-core/cli-proxy-api', 'legacy-binary')
        write(legacy / 'cpa-core/config.yaml', 'legacy-config')
        write(legacy / 'oauth/fixture.json', 'synthetic-legacy-credential')
        for name in ['codex.toml', 'lain-launcher', 'lain.service']:
            write(self.root / name, 'protected-' + name)
        core_hash = probe.digest(self.package / 'proxy/lain-proxy')
        write(self.package / 'proxy/binary-manifest.json', json.dumps({'sha256': core_hash}))
        write(self.package / 'proxy/qualification.json', json.dumps({'sha256': core_hash, 'passed': True}))
        write(self.package / 'gate-status.json', '{"backendQualified":true,"releaseQualified":false}')
        manifest = ''.join(f'{probe.digest(path)}  ./{path.relative_to(self.package)}\n'
                           for path in sorted(self.package.rglob('*')) if path.is_file())
        write(self.package / 'SHA256SUMS', manifest)
        self.options = argparse.Namespace(state_home=self.state, package=self.package,
            package_manifest_sha256=probe.digest(self.package / 'SHA256SUMS'), core_sha256=core_hash,
            allow_unreleased=True, execute=True, clients_quiesced=True, days=30,
            legacy_root=legacy, normal_codex=self.root / 'codex.toml', launcher=self.root / 'lain-launcher',
            unit_file=self.root / 'lain.service', legacy_service='legacy-fixture.service')

        owner = self
        self.real_api = probe.Api

        class FakeApi:
            def __init__(self, state):
                assert state == owner.state

            def ready(self):
                assert owner.service_active
                if (owner.state / 'app/host/Lain.Host').read_text() == 'new-host':
                    owner.write(owner.state / 'usage.db', 'synthetic-migrated-schema-bytes')
                return {'proxyReady': True, 'revision': owner.config['revision'], 'appliedRevision': owner.config['appliedRevision']}

            def get(self, path):
                if path == '/configuration':
                    return owner.config
                if path.startswith('/attempts?'):
                    return {'items': [], 'offset': 0, 'limit': 500, 'total': 0}
                if path.startswith('/usage/analysis?'):
                    return {'contractVersion': 1, 'dimensions': [], 'totals': {'attempts': 0},
                            'performance': {key: {'samples': 0, 'eligible': 0}
                            for key in ('firstOutputLatencyMs', 'totalDurationMs', 'outputTokensPerSecond')}}
                if path.startswith('/usage?'):
                    return {'from': 'fixed-from', 'to': 'fixed-to', 'timeZone': 'UTC', 'cursor': 2,
                            'complete': True, 'totals': {'attempts': 0}, 'daily': [], 'accounts': [], 'models': []}
                if path == '/inspector':
                    return {'contractVersion': 1}
                raise AssertionError('Unexpected API operation')

        self.addCleanup(patch.stopall)
        patch.object(probe, 'Api', FakeApi).start()
        patch.object(probe.time, 'sleep', lambda delay: None).start()
        patch.object(probe, 'service', self.fake_service).start()
        patch.object(probe, 'transition', self.fake_transition).start()

    def fake_service(self, name):
        if name == probe.OWN_SERVICE:
            return {'LoadState': 'loaded', 'ActiveState': 'active' if self.service_active else 'inactive',
                    'SubState': 'running' if self.service_active else 'dead', 'MainPID': '12' if self.service_active else '0',
                    'FragmentPath': str(self.options.unit_file), 'ControlGroup': '',
                    'ExecStart': '{ path=' + str(self.state / 'app/host/Lain.Host') + ' ; }'}
        self.assertEqual(name, self.options.legacy_service)
        return {'LoadState': 'loaded', 'ActiveState': 'active', 'MainPID': '77', 'InvocationID': 'unchanged'}

    def fake_transition(self, action, state, unit):
        self.assertEqual((state, unit), (self.state, self.options.unit_file))
        self.assertIn(action, ('stop', 'start'))
        self.transitions.append(action)
        self.service_active = action == 'start'

    def install(self):
        result = probe.upgrade(self.options)
        self.options.backup = Path(result['backup'])
        self.assertTrue(result['passed'])
        return result

    def test_plan_does_not_mutate_or_operate_services(self):
        before = probe.fingerprint(self.state)
        self.options.execute = False
        self.assertTrue(probe.upgrade(self.options)['planOnly'])
        self.assertEqual(before, probe.fingerprint(self.state))
        self.assertEqual(self.transitions, [])

    def test_segmented_journal_rejected_before_services_or_state_mutation(self):
        journal = self.state / 'proxy/events.jsonl'
        journal.unlink(); journal.mkdir()
        self.write(journal / 'manifest.json', '{"version":1}')
        (self.state / 'proxy/events.jsonl.source').unlink()
        before = probe.fingerprint(self.state)
        for execute in (False, True):
            self.options.execute = execute
            with patch.object(probe, 'own_service', side_effect=AssertionError('service access before format guard')):
                with self.assertRaisesRegex(probe.GuardError, 'journal-export-flat to a NEW destination'):
                    probe.upgrade(self.options)
            self.assertEqual(before, probe.fingerprint(self.state))
            self.assertEqual(self.transitions, [])

    def test_ready_accepts_healthy_pending_configuration(self):
        api = self.real_api(self.state)
        pending = {'proxyReady': True, 'revision': 4, 'appliedRevision': 2, 'state': 'pending-application'}
        with patch.object(api, 'get', return_value=pending), patch.object(probe.time, 'monotonic', side_effect=[0, 0, 50]):
            self.assertEqual(api.ready(), pending)

    def test_ready_rejects_invalid_revision_pairs(self):
        api = self.real_api(self.state)
        for revision, applied in [('4', 2), (True, 0), (2, 3), (-1, 0)]:
            with self.subTest(revision=revision, applied=applied), patch.object(api, 'get',
                    return_value={'proxyReady': True, 'revision': revision, 'appliedRevision': applied}), \
                    patch.object(probe.time, 'monotonic', side_effect=[0, 0, 50]):
                with self.assertRaises(probe.GuardError):
                    api.ready()

    def test_upgrade_and_rollback_preserve_pending_revision_pair(self):
        self.config['appliedRevision'] = 2
        self.write(self.state / 'configuration.json', json.dumps(self.config))
        self.write(self.state / 'active-profile.json', '{"id":"fixture","revision":2}')
        self.write(self.state / 'proxy/runtime.json', '{"valid":true,"revision":2}')
        before = probe.selected_fingerprints(self.state, probe.PRESERVED_PARTS)
        result = self.install()
        self.assertEqual((result['revision'], result['appliedRevision']), (4, 2))
        self.assertEqual(before, probe.selected_fingerprints(self.state, probe.PRESERVED_PARTS))
        self.assertTrue(probe.rollback(self.options)['rolledBack'])
        self.assertEqual(self.config['appliedRevision'], 2)
        self.assertEqual(before, probe.selected_fingerprints(self.state, probe.PRESERVED_PARTS))

    def test_legacy_response_logs_and_temporary_auth_are_excluded(self):
        before = probe.outside_fingerprints(self.options)
        self.write(self.options.legacy_root / 'oauth/logs/private-response.log', 'synthetic-response-content')
        self.write(self.options.legacy_root / 'oauth/rotating.tmp', 'synthetic-temporary-auth')
        self.assertEqual(before, probe.outside_fingerprints(self.options))
        self.write(self.options.legacy_root / 'oauth/fixture.json', 'changed-synthetic-credential')
        self.assertNotEqual(before, probe.outside_fingerprints(self.options))

    def test_upgrade_and_rollback_restore_paired_app_and_database(self):
        outside = probe.outside_fingerprints(self.options)
        self.install()
        self.assertEqual(self.transitions, ['stop', 'start'])
        self.assertEqual((self.state / 'usage.db').read_text(), 'synthetic-migrated-schema-bytes')
        result = probe.rollback(self.options)
        self.assertTrue(result['rolledBack'])
        self.assertEqual((self.state / 'app/host/Lain.Host').read_text(), 'old-host')
        self.assertEqual((self.state / 'usage.db').read_text(), 'synthetic-old-schema-bytes')
        self.assertEqual(outside, probe.outside_fingerprints(self.options))
        self.assertEqual(self.transitions, ['stop', 'start', 'stop', 'start'])

    def test_new_journal_events_block_rewind_and_preserve_new_state(self):
        self.install()
        self.write(self.state / 'proxy/events.jsonl', 'old durable event\nnew charged attempt\n')
        with self.assertRaisesRegex(probe.GuardError, 'new durable events'):
            probe.rollback(self.options)
        self.assertFalse(self.service_active)
        self.assertEqual((self.state / 'usage.db').read_text(), 'synthetic-migrated-schema-bytes')
        self.assertTrue((self.options.backup / 'app.before').is_dir())
        self.assertTrue(list(self.options.backup.glob('state.after-*')))

    def test_rotated_owned_credentials_block_restoration(self):
        self.install()
        self.write(self.state / 'proxy/auth-owned/account.json', '{"access":"rotated-synthetic"}')
        with self.assertRaisesRegex(probe.GuardError, 'credentials/configuration changed'):
            probe.rollback(self.options)
        self.assertIn('rotated-synthetic', (self.state / 'proxy/auth-owned/account.json').read_text())

    def test_rollback_recovers_cutover_gap_without_current_app(self):
        self.install()
        (self.state / 'app').rename(self.options.backup / 'simulated-incomplete-cutover')
        result = probe.rollback(self.options)
        self.assertTrue(result['rolledBack'])
        self.assertEqual((self.state / 'app/host/Lain.Host').read_text(), 'old-host')

    def test_restore_flush_failure_never_starts_old_host(self):
        self.install()
        with patch.object(probe, 'sync_part', side_effect=OSError('synthetic flush failure')):
            with self.assertRaises(OSError):
                probe.rollback(self.options)
        self.assertFalse(self.service_active)
        self.assertEqual(self.transitions, ['stop', 'start', 'stop'])
        record = json.loads((self.options.backup / 'rollback.json').read_text())
        self.assertEqual(record['phase'], 'state-restored')
        self.assertFalse(record['passed'])

    def test_changed_package_and_unapproved_release_fail_before_stop(self):
        self.options.allow_unreleased = False
        with self.assertRaisesRegex(probe.GuardError, 'allow-unreleased'):
            probe.upgrade(self.options)
        self.options.allow_unreleased = True
        self.write(self.package / 'host/Lain.Host', 'unreviewed-host', True)
        with self.assertRaisesRegex(probe.GuardError, 'complete manifest'):
            probe.upgrade(self.options)
        self.assertEqual(self.transitions, [])


if __name__ == '__main__':
    unittest.main()
