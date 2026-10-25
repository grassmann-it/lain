#!/usr/bin/env python3
"""Controlled OWN lain upgrade/rollback. Plan-only unless --execute is explicit.

No installer, inference request, legacy collector, or live SQLite connection is
used. Execution is reserved for the integrator after exact-artifact review.
"""
import argparse
import datetime as dt
import fcntl
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import sys
import time
import uuid
from urllib.parse import urlencode
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener


OWN_SERVICE = 'lain.service'
STATE_PARTS = ('configuration.json', 'active-profile.json', 'credentials', 'imports',
               'proxy', 'usage.db', 'usage.db-wal', 'usage.db-shm')
JOURNAL_PARTS = ('proxy/events.jsonl', 'proxy/events.jsonl.source')
PRESERVED_PARTS = ('configuration.json', 'active-profile.json', 'credentials', 'imports',
                   'proxy/config.yaml', 'proxy/bindings.json', 'proxy/runtime.json',
                   'proxy/auth', 'proxy/auth-owned')
SERVICE_FIELDS = ('LoadState', 'ActiveState', 'SubState', 'MainPID', 'InvocationID',
                  'ActiveEnterTimestampMonotonic', 'ExecMainStartTimestampMonotonic',
                  'NRestarts', 'FragmentPath', 'ControlGroup', 'ExecStart')
LEGACY_ATTEMPT_FIELDS = ('id', 'requestId', 'accountId', 'routeId', 'provider', 'model',
                         'startedAt', 'finishedAt', 'statusCode', 'state', 'tokens',
                         'durationMs', 'firstTokenMs', 'error')
MAX_ATTEMPTS = 50000


class GuardError(Exception):
    pass


def require(condition, label):
    if not condition:
        raise GuardError(label)


def digest(path):
    result = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def safe_path(value):
    path = Path(value)
    require(path.is_absolute() and '..' not in path.parts, 'Paths must be absolute without parent traversal.')
    for candidate in (path, *path.parents):
        require(not candidate.is_symlink(), 'Symbolic-link paths are refused.')
    return path


def fingerprint(path):
    path = safe_path(path)
    if not path.exists():
        return {'missing': True}
    entries = [path] if path.is_file() else [path, *sorted(path.rglob('*'))]
    result = {}
    for entry in entries:
        mode = entry.lstat().st_mode
        require(not stat.S_ISLNK(mode) and (stat.S_ISREG(mode) or stat.S_ISDIR(mode)), 'Special files or symlinks are refused.')
        key = '.' if entry == path else str(entry.relative_to(path))
        result[key] = {'mode': stat.S_IMODE(mode), 'sha256': digest(entry) if entry.is_file() else None}
    return result


def selected_fingerprints(root, parts):
    return {name: fingerprint(root / name) for name in parts}


def write_json(path, value):
    temporary = path.with_name(path.name + '.tmp')
    with temporary.open('w', encoding='utf-8') as stream:
        os.chmod(temporary, 0o600)
        json.dump(value, stream, indent=2, sort_keys=True)
        stream.write('\n'); stream.flush(); os.fsync(stream.fileno())
    temporary.replace(path)
    sync_directory(path.parent)


def sync_directory(path):
    descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def sync_tree(path):
    for entry in sorted(path.rglob('*')):
        if entry.is_file():
            with entry.open('rb') as stream:
                os.fsync(stream.fileno())
    for entry in [*sorted((p for p in path.rglob('*') if p.is_dir()), reverse=True), path]:
        sync_directory(entry)


def sync_part(path):
    if path.is_dir():
        sync_tree(path)
    elif path.exists():
        with path.open('rb') as stream:
            os.fsync(stream.fileno())
    sync_directory(path.parent)


def copy_part(source, destination):
    if not source.exists():
        return
    fingerprint(source)  # Refuse links/special files before following a tree.
    destination.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    if source.is_dir():
        shutil.copytree(source, destination, copy_function=shutil.copy2)
    else:
        shutil.copy2(source, destination)


def service(name):
    require(re.fullmatch(r'[A-Za-z0-9_.@-]+\.service', name) is not None, 'Invalid service name.')
    command = ['systemctl', '--user', 'show', name, *['--property=' + field for field in SERVICE_FIELDS]]
    result = subprocess.run(command, capture_output=True, text=True, timeout=15)
    require(result.returncode == 0, 'Cannot read user service state.')
    values = dict(line.split('=', 1) for line in result.stdout.splitlines() if '=' in line)
    require(values.get('LoadState') == 'loaded', 'Expected user service is not loaded.')
    return values


def own_service(state, unit):
    current = service(OWN_SERVICE)
    require(current['FragmentPath'] == str(unit), 'Own service unit path differs from the reviewed unit.')
    match = re.search(r'\bpath=([^;]+);', current.get('ExecStart', ''))
    require(match is not None and match[1].strip() == str(state / 'app/host/Lain.Host'), 'Service executable is outside the owned installation.')
    return current


def transition(action, state, unit):
    require(action in ('stop', 'start'), 'Only own stop/start transitions are permitted.')
    previous = own_service(state, unit)
    result = subprocess.run(['systemctl', '--user', action, OWN_SERVICE], capture_output=True, timeout=45)
    require(result.returncode == 0, 'Own service transition failed.')
    if action == 'stop':
        current = own_service(state, unit)
        require(current['ActiveState'] == 'inactive' and current['MainPID'] == '0', 'Own Host is not fully stopped.')
        group = previous.get('ControlGroup', '')
        if group:
            require(group.startswith('/') and '..' not in PurePosixPath(group).parts, 'Invalid own service cgroup.')
            processes = Path('/sys/fs/cgroup') / group.lstrip('/') / 'cgroup.procs'
            require(not processes.exists() or not processes.read_text().strip(), 'Owned Core or child process still holds state.')


def package_check(package, expected_manifest, expected_core, allow_unreleased):
    package = safe_path(package)
    require(re.fullmatch('[0-9a-f]{64}', expected_manifest or '') is not None and
            re.fullmatch('[0-9a-f]{64}', expected_core or '') is not None, 'Pin full lowercase SHA-256 values for package and Core.')
    manifest = package / 'SHA256SUMS'
    require(manifest.is_file() and digest(manifest) == expected_manifest, 'Package manifest does not match the reviewed hash.')
    declared = {}
    for line in manifest.read_text().splitlines():
        match = re.fullmatch(r'([0-9a-fA-F]{64}) [ *](.+)', line)
        require(match is not None, 'Invalid checksum manifest line.')
        name = match[2].removeprefix('./')
        relative = PurePosixPath(name)
        require(not relative.is_absolute() and '..' not in relative.parts and '\\' not in name and
                name not in declared and name != 'SHA256SUMS', 'Unsafe or duplicate manifest member.')
        declared[name] = match[1].lower()
    all_entries = fingerprint(package)
    actual = {name: value['sha256'] for name, value in all_entries.items() if value['sha256'] is not None and name != 'SHA256SUMS'}
    if '.lain-package' not in declared:
        actual.pop('.lain-package', None)
    require(actual == declared, 'Package contents differ from the complete manifest.')
    for name in ('cli/Lain.Cli', 'host/Lain.Host', 'desktop/Lain.Desktop', 'proxy/lain-proxy'):
        require(os.access(package / name, os.X_OK), 'Package component is not executable.')
    require(digest(package / 'proxy/lain-proxy') == expected_core, 'Package contains a different Core.')
    binary = json.loads((package / 'proxy/binary-manifest.json').read_text())
    qualified = json.loads((package / 'proxy/qualification.json').read_text())
    gate = json.loads((package / 'gate-status.json').read_text())
    require(binary.get('sha256') == expected_core and qualified.get('sha256') == expected_core and
            qualified.get('passed') is True and gate.get('backendQualified') is True, 'Exact-Core qualification is missing or inconsistent.')
    require(allow_unreleased or gate.get('releaseQualified') is True, 'A staged package requires explicit --allow-unreleased for controlled verification.')
    return {'manifestSha256': expected_manifest, 'coreSha256': expected_core, 'releaseQualified': gate.get('releaseQualified') is True}


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, request, file, code, message, headers, new_url):
        return None


class Api:
    def __init__(self, state):
        self.token_path = state / 'credentials/host.token'
        self.opener = build_opener(ProxyHandler({}), NoRedirect())

    def get(self, path):
        token = self.token_path.read_text().strip()
        request = Request('http://127.0.0.1:18418/api/v1' + path, headers={'Authorization': 'Bearer ' + token})
        with self.opener.open(request, timeout=10) as response:
            require(response.status == 200, 'Owned API read failed.')
            data = response.read(16 * 1024 * 1024 + 1)
        require(len(data) <= 16 * 1024 * 1024, 'Owned API response exceeds the verification bound.')
        return json.loads(data)

    def ready(self):
        deadline = time.monotonic() + 45
        while time.monotonic() < deadline:
            try:
                value = self.get('/status')
                revision, applied = value.get('revision'), value.get('appliedRevision')
                if value.get('proxyReady') is True and type(revision) is int and type(applied) is int and 0 <= applied <= revision:
                    return value
            except (OSError, ValueError, GuardError):
                pass
            time.sleep(.25)
        raise GuardError('Own API/Core did not become ready.')


def without_nulls(value):
    if isinstance(value, dict):
        return {key: without_nulls(item) for key, item in value.items() if item is not None}
    if isinstance(value, list):
        return [without_nulls(item) for item in value]
    return value


def usage(api, window, require_new=False):
    query = urlencode(window)
    summary = api.get('/usage?' + query)
    rows, offset, total = [], 0, None
    while total is None or offset < total:
        page = api.get('/attempts?' + query + '&limit=500&offset=' + str(offset))
        require(page['total'] <= MAX_ATTEMPTS, 'Choose a narrower fixed usage window for the bounded probe.')
        require(total is None or total == page['total'], 'Usage changed while paging; clients must be quiescent.')
        total = page['total']
        require(page['items'] or offset >= total, 'Usage pagination omitted an interval.')
        for item in page['items']:
            require(item['state'] != 'pending', 'Pending attempts prevent a quiescent upgrade baseline.')
            if require_new:
                require(all(key in item for key in ('sourceId', 'requestedModel', 'dimensions', 'measurements', 'completionState')),
                        'New Host attempt contract is missing.')
            rows.append({key: item.get(key) for key in LEGACY_ATTEMPT_FIELDS})
        offset += len(page['items'])
    summary = {key: summary[key] for key in ('from', 'to', 'timeZone', 'cursor', 'complete', 'totals', 'daily', 'accounts', 'models')}
    return {'summary': summary, 'attempts': rows}


def outside_fingerprints(options):
    legacy = options.legacy_root
    require((legacy / 'oauth').is_dir(), 'Expected legacy authentication directory is missing.')
    stable = {name: fingerprint(legacy / name) for name in ('cpa-core/cli-proxy-api', 'cpa-core/config.yaml')}
    require(all(not value.get('missing') for value in stable.values()), 'Expected legacy stable files are missing.')
    auth_root = safe_path(legacy / 'oauth')
    auth = {}
    def walk_error(error):
        raise GuardError('Cannot read stable legacy credential metadata.')
    # Legacy response-log retention continues during unrelated work. Do not even
    # read those payloads; protect only stable auth files and directory modes.
    for directory, names, files in os.walk(auth_root, followlinks=False, onerror=walk_error):
        current = Path(directory)
        if current == auth_root:
            names[:] = [name for name in names if name != 'logs']
        mode = current.lstat().st_mode
        require(stat.S_ISDIR(mode) and not stat.S_ISLNK(mode), 'Unexpected legacy auth directory type.')
        auth[str(current.relative_to(auth_root))] = {'mode': stat.S_IMODE(mode), 'sha256': None}
        for name in names:
            require(not (current / name).is_symlink(), 'Legacy credential directory links are refused.')
        for name in files:
            if name.endswith('.tmp') or current == auth_root and name == 'logs':
                continue
            path = current / name
            require(stat.S_ISREG(path.lstat().st_mode), 'Unexpected legacy credential file type.')
            auth[str(path.relative_to(auth_root))] = {'mode': stat.S_IMODE(path.stat().st_mode), 'sha256': digest(path)}
    stable['oauth'] = auth
    return {'normalCodex': fingerprint(options.normal_codex), 'launcher': fingerprint(options.launcher),
            'unit': fingerprint(options.unit_file), 'legacyStable': stable,
            'legacyExclusions': ['oauth/logs/**', 'oauth/**/*.tmp'],
            'legacyService': service(options.legacy_service)}


def snapshot_state(state, destination):
    destination.mkdir(mode=0o700)
    before = selected_fingerprints(state, STATE_PARTS)
    for name in STATE_PARTS:
        copy_part(state / name, destination / name)
    require(before == selected_fingerprints(state, STATE_PARTS) == selected_fingerprints(destination, STATE_PARTS),
            'Stopped state changed during snapshot.')
    sync_tree(destination)
    return before


def verify_online(api, baseline, config):
    status = api.ready()
    require((status.get('revision'), status.get('appliedRevision')) == (config['revision'], config['appliedRevision']),
            'Desired/applied revision pair changed during upgrade.')
    require(without_nulls(api.get('/configuration')) == without_nulls(config), 'Account/route/profile configuration changed.')
    deadline = time.monotonic() + 30
    while True:
        actual = usage(api, baseline['window'], require_new=True)
        if actual == baseline['usage']:
            break
        require(time.monotonic() < deadline, 'Fixed-window historical usage or attempts changed after upgrade.')
        time.sleep(.5)
    analysis = api.get('/usage/analysis?' + urlencode(baseline['window']))
    require(analysis.get('contractVersion') == 1 and isinstance(analysis.get('dimensions'), list), 'New analysis endpoint/schema is unavailable.')
    for key in ('firstOutputLatencyMs', 'totalDurationMs', 'outputTokensPerSecond'):
        metric = analysis['performance'][key]
        require(0 <= metric['samples'] <= metric['eligible'] == analysis['totals']['attempts'], 'Analysis sample population is inconsistent.')
    require(api.get('/inspector').get('contractVersion') == 1, 'New inspector endpoint is unavailable.')
    return {'revision': status['revision'], 'appliedRevision': status['appliedRevision'],
            'historicalUsageUnchanged': True, 'newSchemaExercisedByAnalysis': True,
            'attemptCount': len(actual['attempts']), 'cursor': actual['summary']['cursor']}


def lock_state(state):
    handle = (state / 'upgrade.lock').open('a')
    os.chmod(handle.name, 0o600)
    fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
    return handle


def upgrade(options):
    state, package = options.state_home, options.package
    require(not (state / 'proxy/events.jsonl').is_dir(),
            'This upgrade workflow is qualified for flat journals only. Before using it, stop the owned runtime separately, run lain-proxy journal-export-flat to a NEW destination, preserve the full current archive, and explicitly review/adopt the matching flat state. No service action was taken by this workflow; other upgrade gates still apply.')
    package_info = package_check(package, options.package_manifest_sha256, options.core_sha256, options.allow_unreleased)
    require((state / 'app/.lain-package').is_file(), 'Existing installation is not an owned lain package.')
    require(state.stat().st_uid == os.getuid() and stat.S_IMODE(state.stat().st_mode) & 0o077 == 0,
            'Owned state must belong to the current user and be private.')
    require(state not in package.parents and package not in state.parents and state != package, 'Package and owned state must not overlap.')
    if not options.execute:
        return {'planOnly': True, 'stateHome': str(state), 'package': str(package), **package_info,
                'ownService': OWN_SERVICE, 'serviceTransitions': ['stop', 'start'], 'automaticRollback': False}
    require(options.clients_quiesced, 'Execution requires --clients-quiesced for an inference/configuration quiet window; read-only Desktop polling is allowed.')
    with lock_state(state):
        current_service = own_service(state, options.unit_file)
        require(current_service['ActiveState'] == 'active', 'Own service must be active for the baseline.')
        api = Api(state); status = api.ready()
        configuration = api.get('/configuration')
        require((status.get('revision'), status.get('appliedRevision')) ==
                (configuration['revision'], configuration['appliedRevision']), 'Configuration changed during the readiness baseline.')
        finish = dt.datetime.now(dt.timezone.utc)
        window = {'from': (finish - dt.timedelta(days=options.days)).isoformat(), 'to': finish.isoformat(), 'timeZone': 'UTC'}
        baseline = {'window': window, 'usage': usage(api, window)}
        time.sleep(1)
        require(baseline['usage'] == usage(api, window), 'Usage baseline is not quiescent.')
        outside = outside_fingerprints(options)
        suffix = finish.strftime('%Y%m%dT%H%M%SZ-') + uuid.uuid4().hex[:8]
        backup = state / 'upgrade-backups' / suffix
        backup.mkdir(parents=True, mode=0o700)
        staging = state / ('.upgrade-stage-' + uuid.uuid4().hex)
        record = {'schemaVersion': 1, 'phase': 'staging', 'passed': False, 'stateHome': str(state),
                  'backup': str(backup), 'staging': str(staging), 'package': package_info, 'configuration': configuration,
                  'baseline': baseline, 'outside': outside, 'ownServiceBefore': current_service,
                  'options': {key: str(getattr(options, key)) for key in ('legacy_root', 'normal_codex', 'launcher', 'unit_file', 'legacy_service')},
                  'inferenceRequestsIssued': 0, 'automaticRollback': False}
        write_json(backup / 'upgrade.json', record)
        try:
            shutil.copytree(package, staging)
            (staging / '.lain-package').touch(mode=0o600)
            for entry in [staging, *staging.rglob('*')]:
                os.chmod(entry, stat.S_IMODE(entry.stat().st_mode) & 0o700)
            package_check(staging, options.package_manifest_sha256, options.core_sha256, options.allow_unreleased)
            sync_tree(staging)
            before_stop = api.ready()
            require((before_stop['revision'], before_stop['appliedRevision']) ==
                    (configuration['revision'], configuration['appliedRevision']) and
                    without_nulls(api.get('/configuration')) == without_nulls(configuration) and
                    usage(api, window) == baseline['usage'], 'Configuration or usage changed before the controlled stop.')
            transition('stop', state, options.unit_file)
            record['phase'] = 'own-service-stopped'; write_json(backup / 'upgrade.json', record)
            require(all((state / name).is_file() for name in ('usage.db', *JOURNAL_PARTS)), 'Owned database/journal/source files are missing after stop.')
            record['stateBefore'] = snapshot_state(state, backup / 'state.before')
            record['journalBefore'] = selected_fingerprints(state, JOURNAL_PARTS)
            record['preservedBefore'] = selected_fingerprints(state, PRESERVED_PARTS)
            require(outside == outside_fingerprints(options), 'Protected external files or legacy service changed during the upgrade interval.')
            record['appBefore'] = fingerprint(state / 'app')
            record['phase'] = 'snapshot-complete'; write_json(backup / 'upgrade.json', record)
            (state / 'app').rename(backup / 'app.before')
            staging.rename(state / 'app'); sync_directory(state); sync_directory(backup)
            record['phase'] = 'app-replaced'; write_json(backup / 'upgrade.json', record)
            transition('start', state, options.unit_file)
            record['verification'] = verify_online(api, baseline, configuration)
            require(record['preservedBefore'] == selected_fingerprints(state, PRESERVED_PARTS), 'Owned credentials or runtime configuration changed.')
            require(outside == outside_fingerprints(options), 'Protected external files or legacy service changed during the upgrade interval.')
            package_check(state / 'app', options.package_manifest_sha256, options.core_sha256, options.allow_unreleased)
            record.update(phase='verified', passed=True)
            write_json(backup / 'upgrade.json', record)
            return {'passed': True, 'backup': str(backup), 'package': package_info,
                    **record['verification'], 'protectedFilesUnchanged': True,
                    'legacyServiceUnchanged': True, 'legacyLogAndTempFilesExcluded': True,
                    'ownCredentialsUnchanged': True, 'inferenceRequestsIssued': 0,
                    'rollbackRequiresStoppedJournalAndCredentialGuard': True}
        except BaseException as error:
            record['phase'] = 'interrupted-' + record['phase']
            record['failure'] = {'type': type(error).__name__,
                                 'reason': str(error) if isinstance(error, GuardError) else 'Internal operation failed; no raw exception was retained.'}
            write_json(backup / 'upgrade.json', record)
            # Never rewind migrated DB/journal or rotating credentials automatically.
            # The explicit rollback command first stops and preserves the new state.
            raise GuardError('Upgrade interrupted; private report retained at ' + str(backup / 'upgrade.json') + '. No automatic rollback was attempted.') from None


def rollback(options):
    backup = safe_path(options.backup)
    record = json.loads((backup / 'upgrade.json').read_text())
    state = safe_path(record['stateHome'])
    require(record.get('schemaVersion') == 1 and (backup / 'app.before/.lain-package').is_file(), 'Rollback snapshot is incomplete.')
    require(record.get('appBefore') == fingerprint(backup / 'app.before') and
            record.get('stateBefore') == selected_fingerprints(backup / 'state.before', STATE_PARTS), 'Rollback snapshot integrity failed.')
    if not options.execute:
        return {'planOnly': True, 'stateHome': str(state), 'backup': str(backup),
                'requiresUnchangedJournalAndCredentials': True, 'serviceTransitions': ['stop', 'start']}
    require(options.clients_quiesced, 'Rollback requires --clients-quiesced.')
    for name, value in record['options'].items():
        setattr(options, name, value if name == 'legacy_service' else safe_path(value))
    with lock_state(state):
        transition('stop', state, options.unit_file)
        failed = backup / ('state.after-' + uuid.uuid4().hex[:8])
        snapshot_state(state, failed)
        rollback_report = {'passed': False, 'newStateArchive': str(failed), 'phase': 'new-state-preserved'}
        write_json(backup / 'rollback.json', rollback_report)
        require(record['journalBefore'] == selected_fingerprints(state, JOURNAL_PARTS),
                'Rollback refused: new durable events/source exist. Own service remains stopped; preserve and reconcile the new journal.')
        require(record['preservedBefore'] == selected_fingerprints(state, PRESERVED_PARTS),
                'Rollback refused: credentials/configuration changed. Do not resurrect rotated credentials or discard changes.')
        require(record['outside'] == outside_fingerprints(options), 'Protected files or legacy service changed; review the interval before rollback.')
        rollback_report['phase'] = 'restoring-app'; write_json(backup / 'rollback.json', rollback_report)
        if (state / 'app').exists():
            archived_app = backup / ('app.after-' + uuid.uuid4().hex[:8])
            (state / 'app').rename(archived_app)
            rollback_report['newAppArchive'] = str(archived_app)
        (backup / 'app.before').rename(state / 'app')
        sync_directory(state); sync_directory(backup)
        rollback_report['phase'] = 'app-restored'; write_json(backup / 'rollback.json', rollback_report)
        displaced = backup / ('state.displaced-' + uuid.uuid4().hex[:8]); displaced.mkdir(mode=0o700)
        rollback_report['displacedStateArchive'] = str(displaced)
        rollback_report['phase'] = 'restoring-state'; write_json(backup / 'rollback.json', rollback_report)
        for name in STATE_PARTS:
            current = state / name
            if current.exists():
                current.rename(displaced / name)
            copy_part(backup / 'state.before' / name, current)
        require(record['stateBefore'] == selected_fingerprints(state, STATE_PARTS), 'Restored stopped state differs from the snapshot.')
        rollback_report['phase'] = 'state-restored'; write_json(backup / 'rollback.json', rollback_report)
        for name in STATE_PARTS:
            sync_part(state / name)
        sync_tree(displaced); sync_directory(backup); sync_directory(state)
        rollback_report['phase'] = 'state-flushed'; write_json(backup / 'rollback.json', rollback_report)
        transition('start', state, options.unit_file)
        api = Api(state); status = api.ready()
        require((status.get('revision'), status.get('appliedRevision')) ==
                (record['configuration']['revision'], record['configuration']['appliedRevision']),
                'Desired/applied revision pair changed during rollback.')
        require(without_nulls(api.get('/configuration')) == without_nulls(record['configuration']) and
                usage(api, record['baseline']['window']) == record['baseline']['usage'], 'Rollback API/state verification failed.')
        require(record['outside'] == outside_fingerprints(options), 'Rollback changed a protected external resource.')
        rollback_report.update(passed=True, phase='verified', oldSchemaRestoredWithMatchingHost=True)
        write_json(backup / 'rollback.json', rollback_report)
        return {'passed': True, 'rolledBack': True, 'backup': str(backup), 'newStateArchive': str(failed),
                'restoredCoreSha256': record['appBefore']['proxy/lain-proxy']['sha256'], 'inferenceRequestsIssued': 0}


def main():
    home = Path.home()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('upgrade', 'rollback'))
    parser.add_argument('--execute', action='store_true')
    parser.add_argument('--clients-quiesced', action='store_true')
    parser.add_argument('--package', type=Path)
    parser.add_argument('--package-manifest-sha256')
    parser.add_argument('--core-sha256')
    parser.add_argument('--allow-unreleased', action='store_true')
    parser.add_argument('--backup', type=Path)
    parser.add_argument('--days', type=int, default=30)
    parser.add_argument('--state-home', type=Path, default=home / '.local/share/lain')
    parser.add_argument('--legacy-root', type=Path, default=home / '.local/share/easycliproxyapi')
    parser.add_argument('--normal-codex', type=Path, default=home / '.codex/config.toml')
    parser.add_argument('--launcher', type=Path, default=home / '.local/bin/lain')
    parser.add_argument('--unit-file', type=Path, default=home / '.config/systemd/user/lain.service')
    parser.add_argument('--legacy-service', default='cliproxyapi.service')
    options = parser.parse_args()
    require(1 <= options.days <= 366, 'Usage verification days must be 1–366.')
    for name in ('state_home', 'legacy_root', 'normal_codex', 'launcher', 'unit_file'):
        setattr(options, name, safe_path(getattr(options, name)))
    require(options.legacy_service != OWN_SERVICE, 'The legacy service cannot be the owned upgrade target.')
    os.umask(0o077)
    if options.action == 'upgrade':
        require(options.package is not None, '--package is required for upgrade planning.')
        options.package = safe_path(options.package)
        result = upgrade(options)
    else:
        require(options.backup is not None, '--backup is required for rollback planning.')
        result = rollback(options)
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    try:
        main()
    except (Exception, KeyboardInterrupt) as error:
        # Never expose arbitrary filesystem/provider/HTTP exception text or bodies.
        print(json.dumps({'passed': False, 'error': type(error).__name__,
                          'detail': str(error) if isinstance(error, GuardError) else 'Upgrade verification failed; inspect private evidence.'}), file=sys.stderr)
        sys.exit(1)
