#!/usr/bin/env python3
"""Synthetic helper checks: no application binary, network, or user state."""
from contextlib import closing
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import sqlite3
import subprocess
import tempfile
import unittest
import runpy

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('actual_packages', Path(__file__).with_name('actual_packages.py'))
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)
HELPER = ROOT / 'scripts/prepare-legacy-rollback.py'


def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()


class LegacyRollback(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='lain-rollback-unit-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.state = self.root / 'current'; self.state.mkdir(mode=0o700)
        self.package = self.root / 'old'; self.package.mkdir(mode=0o700)
        for name in ('host/Lain.Host', 'proxy/lain-proxy'):
            fixture.write(self.package, name, 'synthetic-nonexecutable-binary')
        fixture.write(self.package, 'build-info.json', {'componentSourceCommits': {'host': '413588c44ee5ad730573baab2cd8f6dc328f48c5'}})
        fixture.write(self.package, 'proxy/qualification.json', {'passed': True, 'sha256': sha(self.package / 'proxy/lain-proxy')})
        fixture.write(self.package, 'SHA256SUMS', ''.join(sha(p) + '  ' + str(p.relative_to(self.package)) + '\n'
            for p in sorted(self.package.rglob('*')) if p.is_file()))
        shutil.copytree(self.package, self.state / 'app')
        fixture.seed_configuration(self.state, 'gateway', rotating=True)
        fixture.seed_history(self.state)
        fixture.write(self.state, 'proxy/config.yaml', 'auth-dir: auth\n')
        self.checkpoint = self.root / 'checkpoint'
        self.invoke('checkpoint', self.checkpoint, expected=0)
        with closing(sqlite3.connect(self.state / 'usage.db')) as db, db:
            db.executescript((ROOT / 'scripts/legacy-rollback-schema/004_attempt_error.sql').read_text())
        # Simulate a current rotated grant. Baseline credentials must never return.
        fixture.write(self.state, 'proxy/auth-owned/synthetic-owned.json', {'refresh_token': 'synthetic-current-grant'})

    def invoke(self, action, destination, expected=2, extra=()):
        args = ['python3', str(HELPER), action, '--state', str(self.state), '--destination', str(destination),
            '--old-package', str(self.package), '--old-manifest-sha256', sha(self.package / 'SHA256SUMS'),
            '--old-host-sha256', sha(self.package / 'host/Lain.Host'),
            '--old-core-sha256', sha(self.package / 'proxy/lain-proxy'), '--stopped']
        if action == 'prepare': args += ['--checkpoint', str(self.checkpoint)]
        args += list(extra)
        result = subprocess.run(args, capture_output=True, env=os.environ, timeout=30)
        self.assertEqual(result.returncode, expected, result.stderr.decode())
        if expected: self.assertFalse(destination.exists())

    def test_staged_only_current_grants_and_forward_schema(self):
        before = fixture.upgrade.fingerprint(self.state)
        destination = self.root / 'prepared'; self.invoke('prepare', destination, expected=0)
        self.assertEqual(before, fixture.upgrade.fingerprint(self.state))
        staged = destination / 'rollback-state'
        self.assertEqual((staged / 'proxy/auth-owned/synthetic-owned.json').read_bytes(),
                         (self.state / 'proxy/auth-owned/synthetic-owned.json').read_bytes())
        with closing(sqlite3.connect(staged / 'usage.db')) as db, db:
            self.assertEqual(db.execute('PRAGMA user_version').fetchone()[0], 3)
            self.assertNotIn('error_category', [r[1] for r in db.execute('PRAGMA table_info(attempts)')])
            db.executescript((ROOT / 'scripts/legacy-rollback-schema/004_attempt_error.sql').read_text())
            self.assertEqual(db.execute('PRAGMA user_version').fetchone()[0], 4)

    def test_absolute_auth_rebase_is_explicit_and_exact(self):
        # Change both checkpoint and current consistently, rebuilding checkpoint metadata.
        shutil.rmtree(self.checkpoint)
        with closing(sqlite3.connect(self.state / 'usage.db')) as db, db:
            db.execute('ALTER TABLE attempts DROP COLUMN error_category'); db.execute('PRAGMA user_version=3')
        # DROP reconstructs whitespace: build pristine v3 fixture for an authentic checkpoint.
        (self.state / 'usage.db').unlink(); fixture.seed_history(self.state)
        fixture.write(self.state, 'proxy/config.yaml', 'host: "127.0.0.1"\nauth-dir: ' + json.dumps(str(self.state / 'proxy/auth')) + '\n')
        self.invoke('checkpoint', self.checkpoint, expected=0)
        with closing(sqlite3.connect(self.state / 'usage.db')) as db, db:
            db.executescript((ROOT / 'scripts/legacy-rollback-schema/004_attempt_error.sql').read_text())
        self.invoke('prepare', self.root / 'no-permission')
        destination = self.root / 'rebased'
        self.invoke('prepare', destination, expected=0, extra=('--rebase-auth-dir',))
        expected = 'host: "127.0.0.1"\nauth-dir: ' + json.dumps(str(destination / 'rollback-state/proxy/auth')) + '\n'
        self.assertEqual((destination / 'rollback-state/proxy/config.yaml').read_text(), expected)
        self.assertNotEqual((destination / 'rollback-state/proxy/config.yaml').read_bytes(),
                            (destination / 'current-snapshot/state/proxy/config.yaml').read_bytes())

    def test_reject_error_data(self):
        with closing(sqlite3.connect(self.state / 'usage.db')) as db, db: db.execute("UPDATE attempts SET error_category='unknown'")
        self.invoke('prepare', self.root / 'rejected')

    def test_reject_ambiguous_auth_mappings(self):
        rebase = runpy.run_path(str(HELPER))['rebase_auth_config']
        for extra in ('"auth-dir": /external\n', "'auth-dir': /external\n",
                      '? auth-dir\n: /external\n', '<<: *other\n',
                      '&other\nauth-dir: /external\n'):
            with self.subTest(extra=extra), self.assertRaises(ValueError):
                rebase(('auth-dir: auth\n' + extra).encode(), self.state,
                       self.root / 'staged', True)

    def test_reject_new_event(self):
        with (self.state / 'proxy/events.jsonl').open('a') as stream: stream.write('{}\n')
        self.invoke('prepare', self.root / 'rejected')

    def test_reject_cursor_change(self):
        with closing(sqlite3.connect(self.state / 'usage.db')) as db, db: db.execute('UPDATE sources SET high_watermark=3')
        self.invoke('prepare', self.root / 'rejected')

    def test_reject_pending_recovery(self):
        fixture.write(self.state, 'credentials/recovery/pending.json', {})
        self.invoke('prepare', self.root / 'rejected')

    def test_reject_symlink(self):
        (self.state / 'foreign').symlink_to(self.package)
        self.invoke('prepare', self.root / 'rejected')

    def test_reject_unrecognized_schema(self):
        with closing(sqlite3.connect(self.state / 'usage.db')) as db, db: db.execute('CREATE TABLE extra(value TEXT)')
        self.invoke('prepare', self.root / 'rejected')


if __name__ == '__main__': unittest.main()
