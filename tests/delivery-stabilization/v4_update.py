#!/usr/bin/env python3
"""Plan or execute bounded real-package v4 updates using synthetic private state only.

No systemd, UI, real state, provider request, credential-store import, or Apply.
Only --execute installs packages and starts their Host/Core processes.
"""
import argparse
from contextlib import closing
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import socket
import sqlite3
import tempfile

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('delivery_actual', HERE / 'actual_packages.py')
actual = importlib.util.module_from_spec(spec)
spec.loader.exec_module(actual)
require = actual.require
upgrade = actual.upgrade
PREVIOUS_SOURCE = '793822c17a9bfa6a950bf82c8d2cdb6afa648d7c'
PRESERVED = ('configuration.json', 'active-profile.json', 'proxy/runtime.json', 'proxy/config.yaml',
             'proxy/bindings.json', 'proxy/events.jsonl', 'proxy/events.jsonl.source',
             'credentials', 'proxy/auth', 'proxy/auth-owned', 'ui/workspace-preferences.json')


def fingerprints(home):
    return {name: upgrade.fingerprint(home / name) for name in PRESERVED}


def core_source(package):
    binary = json.loads((package / 'proxy/binary-manifest.json').read_text())
    qualified = json.loads((package / 'proxy/qualification.json').read_text())
    source = binary.get('sourceManifestSha256')
    require(re.fullmatch('[0-9a-f]{64}', source or '') is not None and
            source == qualified.get('sourceManifestSha256'), 'Core source manifest qualification differs')
    require(re.fullmatch('[0-9a-f]{40}', qualified.get('sourceCommit') or '') is not None,
            'Core qualification source commit is missing')
    identity = {name: binary.get(name) for name in
                ('sourceManifestSha256', 'upstreamCommit', 'patchSetSha256')}
    require(all(identity.values()), 'Core source identity is incomplete')
    return dict(identity, qualificationSourceCommit=qualified['sourceCommit'])


def package_plan(args):
    require(re.fullmatch('[0-9a-f]{40}', args.new_source_commit) is not None, 'Pin the full new source commit')
    packages = {}
    for prefix, source in (('new', args.new_source_commit), ('previous', PREVIOUS_SOURCE)):
        for mode in ('monitor', 'gateway'):
            path = getattr(args, prefix + '_' + mode + '_package')
            pin = getattr(args, prefix + '_' + mode + '_manifest_sha256')
            require(re.fullmatch('[0-9a-f]{64}', pin) is not None, 'Pin full package manifest SHA-256')
            core = getattr(args, prefix + '_core_sha256') if mode == 'gateway' else None
            info = actual.package_info(path, pin, mode, core)
            require(info['sourceCommit'] == source, 'Package source differs from reviewed commit')
            require(isinstance(info['components'], dict) and set(info['components']) == {'cli', 'host', 'desktop'},
                    'Complete application component identities are required')
            require(all(component.get('sourceCommit') == source for component in info['components'].values()),
                    'Component source differs from package commit')
            packages[prefix + mode.title()] = info
    require(args.new_source_commit != PREVIOUS_SOURCE, 'The update must contain a different application source')
    for prefix in ('new', 'previous'):
        require(packages[prefix + 'Monitor']['components'] == packages[prefix + 'Gateway']['components'],
                'Monitor and Gateway application components differ')
    old_source = core_source(args.previous_gateway_package)
    new_source = core_source(args.new_gateway_package)
    same = args.new_core_sha256 == args.previous_core_sha256
    if same:
        require(old_source == new_source, 'Reused Core binary requires identical qualified source identity')
        require((args.previous_gateway_package / 'proxy/qualification.json').read_bytes() ==
                (args.new_gateway_package / 'proxy/qualification.json').read_bytes(),
                'Reused Core requires the same existing qualification record')
    return {'packages': packages, 'sameQualifiedCore': same, 'coreSources': {'previous': old_source, 'new': new_source}}


def assert_app(home, package):
    files = lambda root: {name: item['sha256'] for name, item in upgrade.fingerprint(root).items()
                          if item['sha256'] is not None and name != '.lain-package'}
    require(files(home / 'app') == files(package), 'Installed application differs from pinned complete package')


def seed(home, mode, host):
    actual.seed_configuration(home, mode, rotating=True)
    cfg = json.loads((home / 'configuration.json').read_text())
    cfg['Revision'] = 19
    actual.write(home, 'configuration.json', cfg)
    actual.seed_history(home)
    # A real v4 baseline, not the separate legacy schema3 rollback fixture.
    with closing(sqlite3.connect(home / 'usage.db')) as db, db:
        db.executescript((actual.ROOT / 'src/Lain.Host/Usage/Migrations/004_attempt_error.sql').read_text())
    (home / 'proxy/auth').mkdir(mode=0o700, exist_ok=True)
    actual.write(home, 'proxy/config.yaml', 'host: "127.0.0.1"\nport: ' + str(host.core_port) +
                 '\nauth-dir: ' + json.dumps(str(home / 'proxy/auth')) +
                 '\napi-keys: ["synthetic-local-inference"]\n')
    # Exercise an occupied app.previous without importing an older user's package.
    actual.write(home, 'app.previous/.lain-package', '')
    actual.write(home, 'app.previous/synthetic-prior-app.txt', 'Private archive fixture, no executable\n')


def database(home):
    path = home / 'usage.db'
    require(not any(Path(str(path) + suffix).exists() for suffix in ('-wal', '-shm', '-journal')),
            'Synthetic database must be fully stopped/checkpointed')
    with closing(sqlite3.connect(path.as_uri() + '?mode=ro&immutable=1', uri=True)) as db:
        require(db.execute('PRAGMA user_version').fetchone()[0] == 4, 'Expected schema4 throughout v4 update')
        require(db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok' and
                not db.execute('PRAGMA foreign_key_check').fetchall(), 'Synthetic database integrity failed')
        schema = db.execute("SELECT type,name,tbl_name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name").fetchall()
        tables = {}
        for (table,) in db.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name"):
            # Poll-only source observation time changes; durable identity/cursors/digests must not.
            columns = [r[1] for r in db.execute('PRAGMA table_info("' + table + '")')
                       if not (table == 'sources' and r[1] == 'observed_ms')]
            names = ','.join('"' + c + '"' for c in columns)
            tables[table] = sorted(db.execute('SELECT ' + names + ' FROM "' + table + '"').fetchall(), key=repr)
        return schema, tables


def assert_live(host, source, core_hash, mode):
    status = host.api('/status')
    require(source in status['version'], 'Running Host source identity differs')
    require((status['revision'], status['appliedRevision']) == (19, 2), 'Desired19/applied2 changed')
    require(status['runtimeMode'] == mode and bool(status['proxyReady']) == (mode == 'gateway'),
            'Saved runtime intent/Core readiness changed')
    require(status.get('activeProfileId') is None and status.get('activeProfileRevision') is None,
            'Unknown legacy profile was invented')
    if mode == 'gateway':
        require((status.get('application') or {}).get('state') == 'legacy-preserved', 'Legacy runtime was not preserved')
        children = (Path('/proc') / str(host.process.pid) / 'task' / str(host.process.pid) / 'children').read_text().split()
        require(any(upgrade.digest(Path('/proc') / pid / 'exe') == core_hash for pid in children),
                'Running Core child differs from qualified binary')
    else:
        require(not (host.home / 'app/proxy').exists(), 'Monitor-only update installed a Core')
        with socket.socket() as probe:
            require(probe.connect_ex(('127.0.0.1', host.core_port)) != 0, 'Monitor started an inference listener')
    config = host.api('/configuration')
    require((config['revision'], config['appliedRevision']) == (19, 2) and len(config['accounts']) == 2,
            'Synthetic account identities/configuration changed')
    usage = host.api('/usage?from=2026-10-23T00:00:00Z&to=2026-10-24T00:00:00Z&timeZone=UTC')
    totals = usage['totals']
    require(usage['cursor'] == 2 and totals['attempts'] == 1, 'Durable history cursor/attempts changed')
    require(all(totals['tokens'][key] == value for key, value in
                {'input': 72000, 'output': 9986, 'cached': 18000, 'reasoning': 2000, 'total': 81986}.items()),
            'Historical token components changed')


def stopped_backup_and_archive(root, home):
    before = fingerprints(home)
    prior = upgrade.fingerprint(home / 'app.previous')
    helper = home / 'app/packaging/lain-maintenance.py'
    snapshot = root / 'fresh-v4-snapshot'
    actual.command(['python3', str(helper), 'backup', str(home), str(snapshot), '--stopped'], actual.environment(root, home))
    require(fingerprints(snapshot / 'state') == before, 'Full snapshot omitted current synthetic state')
    require(upgrade.fingerprint(snapshot / 'state/app.previous') == prior, 'Full snapshot omitted prior app fixture')
    archived = root / 'archived-app.previous'
    require(not archived.exists(), 'Archive destination must be fresh')
    (home / 'app.previous').rename(archived)
    require(upgrade.fingerprint(archived) == prior and not (home / 'app.previous').exists(), 'Prior app archive differs')
    return snapshot, archived


def run_mode(args, root, mode):
    home = root / 'state'
    previous = getattr(args, 'previous_' + mode + '_package')
    new = getattr(args, 'new_' + mode + '_package')
    actual.install(previous, root, home)
    assert_app(home, previous)
    host = actual.Host(root, home)
    seed(home, mode, host)
    before_start = fingerprints(home)
    with host:
        assert_live(host, PREVIOUS_SOURCE, args.previous_core_sha256, mode)
    # Host management tokens are first created by baseline startup; grants/config must survive it.
    require(before_start['proxy/auth-owned'] == upgrade.fingerprint(home / 'proxy/auth-owned'), 'Baseline changed synthetic owned grants')
    preserved, history = fingerprints(home), database(home)
    snapshot, archived = stopped_backup_and_archive(root, home)
    snapshot_inventory = upgrade.fingerprint(snapshot)
    actual.install(new, root, home)
    assert_app(home, new)
    require(fingerprints(home) == preserved and database(home) == history, 'Installer changed synthetic current state')
    require((home / 'app.previous/.lain-package').exists(), 'Update did not retain previous v4 app')
    with host:
        assert_live(host, args.new_source_commit, args.new_core_sha256, mode)
    require(fingerprints(home) == preserved and database(home) == history, 'New Host changed protected fixture state/history')
    # Simulate a newer grant after update while every process is stopped. Returning
    # application binaries must preserve this latest grant, never restore snapshot grants.
    actual.write(home, 'proxy/auth-owned/synthetic-owned.json', dict(type='codex', account_id='fixture-subject',
        access_token='synthetic-current-access', refresh_token='synthetic-current-refresh', expired='2099-01-01T00:00:00Z'))
    latest = fingerprints(home)
    displaced = root / 'new-app-retained'
    require(not displaced.exists(), 'Application return destination must be fresh')
    (home / 'app').rename(displaced)
    (home / 'app.previous').rename(home / 'app')
    assert_app(home, previous)
    require(fingerprints(home) == latest and database(home) == history, 'App-only return changed latest state')
    with host:
        assert_live(host, PREVIOUS_SOURCE, args.previous_core_sha256, mode)
    require(fingerprints(home) == latest and database(home) == history, 'Previous v4 application cannot retain this fixture')
    actual.install(new, root, home)
    assert_app(home, new)
    with host:
        assert_live(host, args.new_source_commit, args.new_core_sha256, mode)
    require(fingerprints(home) == latest and database(home) == history, 'Forward update changed latest synthetic state')
    require(upgrade.fingerprint(snapshot) == snapshot_inventory, 'Fresh full snapshot was modified')
    require(upgrade.fingerprint(archived) == upgrade.fingerprint(snapshot / 'state/app.previous'),
            'Archived earlier app was changed or lost')
    return mode + ': previous-v4→new→previous-v4 app-only→new; desired19/applied2, unknown profile, history, latest synthetic grants retained'


def execute(args):
    actual.require_loopback_namespace(args.parent_netns)
    with tempfile.TemporaryDirectory(prefix='lain-v4-update-qualification-') as directory:
        root = Path(directory); root.chmod(0o700)
        return [run_mode(args, root / mode, mode) for mode in ('gateway', 'monitor')]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for prefix in ('new', 'previous'):
        for mode in ('monitor', 'gateway'):
            parser.add_argument('--' + prefix + '-' + mode + '-package', required=True, type=Path)
            parser.add_argument('--' + prefix + '-' + mode + '-manifest-sha256', required=True)
        parser.add_argument('--' + prefix + '-core-sha256', required=True)
    parser.add_argument('--new-source-commit', required=True)
    parser.add_argument('--parent-netns', help='Trusted launcher identity captured before unshare: net:[digits]')
    parser.add_argument('--execute', action='store_true')
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args(); os.umask(0o077)
    args.report = upgrade.safe_path(args.report)
    require(not args.report.exists(), 'Report path must be new')
    plan = package_plan(args)
    checks = execute(args) if args.execute else []
    if args.execute:
        require(package_plan(args) == plan, 'Pinned package inputs changed during qualification')
    with args.report.open('x') as stream:
        json.dump(dict(format=1, status='passed' if args.execute else 'not_run', planOnly=not args.execute,
            **plan, checks=checks, scope='Synthetic isolated schema4 package update and bounded app-only return; no real state, Apply, UI, systemd, inference or provider access',
            rollbackCompatibility='Only the executed desired19/applied2 synthetic fixture; no general downgrade or rotating-grant recovery claim'), stream, indent=2)
        stream.write('\n')
    print('PASS bounded v4 package update fixture' if args.execute else 'PLANNED; no install or process executed')


if __name__ == '__main__':
    try: main()
    except (RuntimeError, upgrade.GuardError, OSError, ValueError, KeyError, sqlite3.Error) as error:
        # Do not forward child output, API bodies or private credential fingerprints.
        print('V4 package qualification refused: ' + str(error), file=__import__('sys').stderr)
        raise SystemExit(2)
