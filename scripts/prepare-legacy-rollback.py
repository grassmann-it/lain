#!/usr/bin/env python3
"""Prepare one reviewed legacy rollback in new private state; never mutate input or start binaries."""
import argparse
from contextlib import closing
import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import shutil
import sqlite3
import sys
import tempfile

HERE = Path(__file__).resolve().parent
maintenance = runpy.run_path(str(HERE / 'lain-maintenance.py'))
OLD_HOST_SOURCE = '413588c44ee5ad730573baab2cd8f6dc328f48c5'
SCHEMA_PINS = {'001_usage.sql': '3e5dbdad2260a7559f5f55b1092287793aab0182e05e6d045545b8871222fa1c', '002_source_coverage.sql': 'a39c944bd39c24ad63eaa0f9b34ef888fe1c8a02d0e85a794fce8fb79646847a', '003_attempt_dimensions.sql': 'cd2b71f157ca784ca9b29bd8614bf58cd7c312c69d299b93321ddf54c88ee6fe', '004_attempt_error.sql': 'ddeecbffbb5d50ac3b0be6e8174c015c18782922eaf2111b4ce68036227c3ee5'}
FROZEN = ('configuration.json', 'active-profile.json', 'proxy/runtime.json', 'proxy/config.yaml',
          'proxy/bindings.json', 'proxy/events.jsonl', 'proxy/events.jsonl.source')
EVENT_FIELDS = set('schemaVersion sourceId sequence eventId kind requestId attemptId accountId credentialId routeId observedAt provider requestedModel model outcome inputTokens outputTokens totalTokens cacheReadTokens cacheCreationTokens reasoningTokens usageReported statusCode latencyMs ttftMs dimensions measurements completionState'.split())


def need(ok, text):
    if not ok: raise ValueError(text)


def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()


def package(args):
    root = args.old_package
    maintenance['inventory'](root)
    need(digest(root / 'SHA256SUMS') == args.old_manifest_sha256, 'Old package manifest pin differs')
    listed = {}
    for line in (root / 'SHA256SUMS').read_text().splitlines():
        match = re.fullmatch(r'([0-9a-f]{64}) [ *](.+)', line)
        need(match is not None, 'Old package manifest invalid')
        name = match[2].removeprefix('./'); relative = Path(name)
        need(not relative.is_absolute() and '..' not in relative.parts and name not in listed, 'Unsafe old package path')
        listed[name] = match[1]
    actual = {str(p.relative_to(root)): digest(p) for p in root.rglob('*') if p.is_file() and p.name not in ('SHA256SUMS', '.lain-package')}
    need(actual == listed, 'Old package inventory differs')
    need(digest(root / 'host/Lain.Host') == args.old_host_sha256 and digest(root / 'proxy/lain-proxy') == args.old_core_sha256, 'Old binary pin differs')
    info = json.loads((root / 'build-info.json').read_text())
    need(info.get('componentSourceCommits', {}).get('host') == OLD_HOST_SOURCE, 'Unreviewed old Host source')
    qualification = json.loads((root / 'proxy/qualification.json').read_text())
    need(qualification.get('passed') is True and qualification.get('sha256') == args.old_core_sha256, 'Old Core qualification differs')


def normalized_sql(value):
    # Preserve SQL literals exactly; only trim outer whitespace.
    return (value or '').strip()


def schema(db):
    return [(kind, name, table, normalized_sql(sql)) for kind, name, table, sql in db.execute(
        "SELECT type,name,tbl_name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name")]


def expected(version, dropped=False):
    db = sqlite3.connect(':memory:')
    for number in range(1, version + 1):
        files = list((HERE / 'legacy-rollback-schema').glob(f'{number:03d}_*.sql'))
        need(len(files) == 1 and digest(files[0]) == SCHEMA_PINS.get(files[0].name), 'Rollback schema resource missing or changed')
        db.executescript(files[0].read_text())
    if dropped: db.execute('ALTER TABLE attempts DROP COLUMN error_category')
    value = schema(db); db.close(); return value


def open_db(path, version):
    need(path.is_file() and not any(Path(str(path) + suffix).exists() for suffix in ('-wal', '-shm', '-journal')), 'Checkpoint database must be closed and checkpointed')
    db = sqlite3.connect(path.as_uri() + '?mode=ro&immutable=1', uri=True)
    try:
        need(db.execute('PRAGMA user_version').fetchone()[0] == version, 'Unexpected database version')
        need(db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok' and not db.execute('PRAGMA foreign_key_check').fetchall(), 'Database integrity failed')
        need(schema(db) == expected(version), 'Unrecognized database schema')
        return db
    except BaseException:
        db.close(); raise


def legacy_state(state):
    maintenance['inventory'](state); maintenance['stopped'](state)
    need(not any((state / name).exists() for name in ('restore-requires-reconnect', 'profile-application.json')), 'Modern recovery/apply metadata blocks old Host')
    cfg = json.loads((state / 'configuration.json').read_text())
    need(cfg.get('Revision') == 11 and cfg.get('AppliedRevision') == 2 and cfg.get('RuntimeMode', 'gateway') == 'gateway', 'Expected unchanged legacy desired11/applied2 Gateway')
    need(not any(a.get('Credentials') or a.get('Capabilities') or a.get('Generation', 0) for a in cfg['Accounts']), 'Modern accounts block old Host')
    need(not any((state / 'credentials' / name).exists() for name in ('generations', 'operations', 'quarantine', 'recovery', 'material')), 'Modern credential metadata blocks old Host')
    source = (state / 'proxy/events.jsonl.source').read_text()
    journal = (state / 'proxy/events.jsonl').read_text()
    need(not journal or journal.endswith('\n'), 'Journal has an incomplete final record')
    for number, line in enumerate(journal.splitlines(), 1):
        event = json.loads(line)
        need(set(event) <= EVENT_FIELDS and event.get('schemaVersion') == 1 and event.get('sourceId') == source
             and event.get('sequence') == number and event.get('eventId') == source + ':' + str(number), 'Journal is not reviewed legacy format')


def rows(db, tables):
    result = {}
    for table, columns in tables.items():
        # Names originate only from the schema already matched against trusted resources.
        names = ','.join('"' + name + '"' for name in columns)
        result[table] = sorted(db.execute('SELECT ' + names + ' FROM "' + table + '"').fetchall(), key=repr)
    return result


def checkpoint(args):
    legacy_state(args.state)
    with closing(open_db(args.state / 'usage.db', 3)): pass
    temp = Path(tempfile.mkdtemp(prefix='.legacy-checkpoint-', dir=args.destination.parent))
    try:
        snapshot = temp / 'checkpoint'
        maintenance['backup'](args.state, snapshot)
        copied = snapshot / 'state'
        legacy_state(copied)
        with closing(open_db(copied / 'usage.db', 3)): pass
        old_files = maintenance['inventory'](args.old_package)
        installed = maintenance['inventory'](copied / 'app')
        old_files.pop('.lain-package', None); installed.pop('.lain-package', None)
        need(old_files == installed, 'Checkpoint application is not the exact old package')
        package(args)
        (snapshot / 'rollback-checkpoint.json').write_text(json.dumps({'format': 1,
            'oldManifestSha256': args.old_manifest_sha256, 'oldHostSha256': args.old_host_sha256,
            'oldCoreSha256': args.old_core_sha256, 'originalStatePath': str(args.state)}) + '\n')
        snapshot.rename(args.destination)
    finally:
        shutil.rmtree(temp)


def validate_current(baseline, current_state):
    legacy_state(current_state); legacy_state(baseline)
    need(all((baseline / name).read_bytes() == (current_state / name).read_bytes() for name in FROZEN), 'Configuration or journal changed since checkpoint')
    with closing(open_db(baseline / 'usage.db', 3)) as old, closing(open_db(current_state / 'usage.db', 4)) as current:
        need(current.execute('SELECT COUNT(*) FROM attempts WHERE error_category IS NOT NULL').fetchone()[0] == 0, 'New error data cannot be discarded')
        tables = {name: [r[1] for r in old.execute('PRAGMA table_info("' + name + '")') if not (name == 'sources' and r[1] == 'observed_ms')]
                  for (name,) in old.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")}
        baseline_rows = rows(old, tables)
        need(baseline_rows == rows(current, tables), 'History/source/cursor/digests changed since checkpoint')
    return tables, baseline_rows


def rebase_auth_config(data, original_home, final_home, authorized):
    # Only the compiler's single top-level scalar is accepted; no YAML aliases,
    # nested key, comment, multiline scalar, or arbitrary path rewrite.
    lines = data.decode().splitlines(keepends=True)
    need(not any(re.match(r'''^["'?&*!]|^<<\s*:''', line) for line in lines),
         'Quoted/explicit root keys, aliases, tags and merges are unsupported')
    positions = [i for i, line in enumerate(lines) if re.match(r'^auth-dir\s*:', line)]
    need(len(positions) == 1, 'Expected one top-level auth-dir scalar')
    i = positions[0]; line = lines[i]; body = line.rstrip('\r\n')
    match = re.fullmatch(r'auth-dir: ([^\r\n]+)', body)
    need(match is not None, 'Unreviewed auth-dir syntax')
    scalar = match[1]
    if scalar.startswith('"'):
        value = json.loads(scalar)
    elif scalar.startswith("'") and scalar.endswith("'"):
        value = scalar[1:-1].replace("''", "'")
    else:
        need(not any(c in scalar for c in '#&*!{}[]\\'), 'Unreviewed auth-dir scalar')
        value = scalar
    if value == 'auth': return data
    need(authorized and value == str(original_home / 'proxy/auth'), 'Absolute auth-dir requires explicit exact own-home rebasing')
    ending = line[len(body):]
    lines[i] = 'auth-dir: ' + json.dumps(str(final_home / 'proxy/auth')) + ending
    return ''.join(lines).encode()


def prepare(args):
    maintenance['inventory'](args.checkpoint)
    manifest = json.loads((args.checkpoint / 'snapshot.json').read_text())
    baseline = args.checkpoint / 'state'
    need(maintenance['inventory'](baseline) == manifest['files'], 'Checkpoint state changed')
    pin = json.loads((args.checkpoint / 'rollback-checkpoint.json').read_text())
    original_home = Path(pin['originalStatePath'])
    need(original_home.is_absolute() and '..' not in original_home.parts, 'Invalid checkpoint home')
    need(pin == {'format': 1, 'oldManifestSha256': args.old_manifest_sha256, 'oldHostSha256': args.old_host_sha256,
                 'oldCoreSha256': args.old_core_sha256, 'originalStatePath': str(original_home)}, 'Checkpoint pins differ')
    tables, baseline_rows = validate_current(baseline, args.state)
    maintenance['stopped'](args.state)
    temp = Path(tempfile.mkdtemp(prefix='.legacy-rollback-', dir=args.destination.parent))
    try:
        maintenance['backup'](args.state, temp / 'current-snapshot')
        shutil.copytree(temp / 'current-snapshot/state', temp / 'rollback-state')
        staged = temp / 'rollback-state'
        validate_current(baseline, staged)
        original_yaml = (staged / 'proxy/config.yaml').read_bytes()
        staged_yaml = rebase_auth_config(original_yaml, original_home, args.destination / 'rollback-state', args.rebase_auth_dir)
        (staged / 'proxy/config.yaml').write_bytes(staged_yaml)
        # Only this disposable staged app is replaced. Credentials/auth come from CURRENT.
        shutil.rmtree(staged / 'app'); shutil.copytree(args.old_package, staged / 'app')
        (staged / 'app/.lain-package').touch(mode=0o600)
        package(args)
        app_files = maintenance['inventory'](staged / 'app')
        old_files = maintenance['inventory'](args.old_package)
        app_files.pop('.lain-package', None); old_files.pop('.lain-package', None)
        need(app_files == old_files, 'Staged old application differs')
        db = sqlite3.connect(staged / 'usage.db')
        try:
            db.execute('BEGIN IMMEDIATE'); db.execute('ALTER TABLE attempts DROP COLUMN error_category'); db.execute('PRAGMA user_version=3')
            need(schema(db) == expected(4, dropped=True), 'Converted schema differs')
            need(db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok' and not db.execute('PRAGMA foreign_key_check').fetchall(), 'Converted integrity failed')
            need(rows(db, tables) == baseline_rows, 'Converted history differs')
            db.commit()
        except BaseException: db.rollback(); raise
        finally: db.close()
        retained = maintenance['inventory'](staged)
        original = json.loads((temp / 'current-snapshot/snapshot.json').read_text())['files']
        unchanged = lambda items: {k: v for k, v in items.items() if k not in ('usage.db', 'proxy/config.yaml') and not k.startswith('app/')}
        need(unchanged(retained) == unchanged(original), 'Staged current state differs outside reviewed app/database/auth-dir changes')
        need((staged / 'proxy/config.yaml').read_bytes() == staged_yaml, 'Staged YAML changed beyond reviewed auth-dir rebasing')
        need(maintenance['inventory'](staged / 'credentials') == maintenance['inventory'](args.state / 'credentials'), 'Current credentials changed')
        need(maintenance['inventory'](staged / 'proxy/auth-owned') == maintenance['inventory'](args.state / 'proxy/auth-owned'), 'Current owned grants changed')
        maintenance['stopped'](args.state)
        # Check again after copies: no writer may have altered the inputs.
        need(maintenance['inventory'](args.state) == json.loads((temp / 'current-snapshot/snapshot.json').read_text())['files'], 'Current state changed while staging')
        need(maintenance['inventory'](baseline) == manifest['files'], 'Checkpoint changed while staging')
        package(args)
        (temp / 'preparation.json').write_text(json.dumps({'format': 1, 'status': 'prepared-not-runtime-qualified',
            'scope': 'Exact legacy11/2 schema4-to3 on staged copy; current credentials retained', 'oldHostSource': OLD_HOST_SOURCE, 'authDirRebased': staged_yaml != original_yaml}) + '\n')
        temp.rename(args.destination)
    finally:
        if temp.exists(): shutil.rmtree(temp)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('checkpoint', 'prepare'))
    for name in ('state', 'destination', 'old-package'): parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--checkpoint', type=Path)
    for name in ('old-manifest-sha256', 'old-host-sha256', 'old-core-sha256'): parser.add_argument('--' + name, required=True)
    parser.add_argument('--rebase-auth-dir', action='store_true', help='Permit only original own-home auth-dir scalar rebasing into staged home')
    parser.add_argument('--stopped', action='store_true'); args = parser.parse_args(); os.umask(0o077)
    need(args.stopped, 'Every writer must be stopped')
    for name in ('state', 'destination', 'old_package', 'checkpoint'):
        value = getattr(args, name)
        if value is not None:
            need(value.is_absolute() and '..' not in value.parts, 'Absolute paths without parent traversal required')
            maintenance['safe_path'](value)
    for source in (args.state, args.old_package, args.checkpoint):
        if source is not None:
            maintenance['outside'](source, args.destination)
            need(args.destination not in source.parents, 'Destination must not enclose an input')
    need(args.destination.parent.stat().st_uid == os.getuid(), 'Destination parent must be owned')
    package(args)
    if args.action == 'checkpoint': checkpoint(args)
    else:
        need(args.checkpoint is not None, 'Explicit checkpoint required'); prepare(args)
    print('Prepared private ' + args.action + '; no application started')


if __name__ == '__main__':
    try: main()
    except (ValueError, OSError, KeyError, sqlite3.Error) as error:
        print('Legacy rollback refused: ' + str(error), file=sys.stderr); raise SystemExit(2)
