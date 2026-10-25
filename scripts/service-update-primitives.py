#!/usr/bin/env python3
"""Portable own-installation update primitives; imported by service-update.py.

Source preparation only: requires independent review and exact-package runtime
qualification before execution. Previous and new source commits are explicit.

Never applies configuration, opens/closes UI, inspects SQLite/credential JSON,
restores old grants, changes another service, or performs automatic rollback.
Package/CLI metadata and opaque private file hashes are verification evidence.
"""
from contextlib import contextmanager
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import stat
import subprocess
import threading

# Set once by the portable operator after validating explicit installation inputs.
STATE = PREFIX = LAUNCHER = CONFIG_HOME = LEASE = None
EXTERNAL = ()
FROZEN = ('configuration.json', 'active-profile.json', 'proxy/runtime.json',
          'proxy/config.yaml', 'proxy/bindings.json', 'proxy/events.jsonl', 'proxy/events.jsonl.source')


def need(ok, message):
    if not ok: raise RuntimeError(message)


def safe(path):
    need(path.is_absolute() and '..' not in path.parts, 'Absolute paths without traversal required')
    for p in (path, *path.parents): need(not p.is_symlink(), 'Symlink paths refused')
    return path


def sha(path):
    safe(path)
    need(path.is_file() and path.stat().st_uid == os.getuid(), 'Expected owned regular file')
    with path.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


@contextmanager
def updater_lease():
    lock = safe(LEASE)
    parent = lock.parent.stat()
    need(stat.S_ISDIR(parent.st_mode) and parent.st_uid == os.getuid() and not parent.st_mode & 0o022,
         'Updater lease parent must be owned and protected from other writers')
    descriptor = os.open(lock, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
    try:
        info = os.fstat(descriptor)
        need(stat.S_ISREG(info.st_mode) and info.st_uid == os.getuid() and info.st_nlink == 1 and
             stat.S_IMODE(info.st_mode) == 0o600, 'Updater lease must be an owned private regular file')
        try: fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError: raise RuntimeError('Another v4 updater holds the exclusive lease')
        yield
    finally:
        os.close(descriptor)  # Release only our lease, after all failure cleanup.


def validate_unit_overrides(values):
    environment = shlex.split(values['Environment'])
    homes = [item.partition('=')[2] for item in environment if item.partition('=')[0] == 'LAIN_HOME']
    need(homes == [str(STATE)], 'Loaded own-unit LAIN_HOME must select exactly this own state')
    proxies = [item.partition('=')[2] for item in environment if item.partition('=')[0] == 'LAIN_PROXY_PATH']
    unset = shlex.split(values['UnsetEnvironment'])
    need(proxies == [str(STATE / 'app/proxy/lain-proxy')] or (not proxies and 'LAIN_PROXY_PATH' in unset),
         'Loaded own-unit Core override must select the exact own Core or be explicitly removed')
    need(not any(item.partition('=')[0].startswith('LAIN_REFERENCE_') for item in environment),
         'Loaded own-unit reference overrides are not allowed')
    need(not any(item.partition('=')[0] == 'LAIN_HOME' for item in unset),
         'Loaded own-unit unset rule can remove its own-state selection')
    for hook in ('ExecStartPre', 'ExecStartPost', 'ExecStop', 'ExecStopPost'):
        need(hook in values and not values[hook].strip(), 'Loaded own-unit lifecycle hooks must be explicitly empty')


def private_json(path, value):
    temp = path.with_name(path.name + '.new')
    with temp.open('x') as stream: json.dump(value, stream, indent=2); stream.write('\n')
    temp.replace(path)


def recognized_unreadable_external(proc, comm):
    # Replaced by finite platform proof by the operator, fail closed by default.
    return False


def processes():
    result = []
    candidates = {'Lain.Desktop', 'Lain.Host', 'Lain.Cli', 'lain-proxy', 'dotnet'}
    for p in Path('/proc').iterdir():
        if not p.name.isdigit() or int(p.name) == os.getpid(): continue
        comm = None
        candidate = False
        try:
            if p.stat().st_uid != os.getuid(): continue
            comm = (p / 'comm').read_text().strip()
            candidate = comm in candidates
            exe = os.readlink(p / 'exe').removesuffix(' (deleted)')
            candidate = candidate or Path(exe).name in candidates
            # Read all accessible own executables. Known app candidates require
            # available argv as well; argument/environment values are never logged.
            desktop = comm == 'Lain.Desktop' or Path(exe).name == 'Lain.Desktop'
            if candidate:
                arguments = (p / 'cmdline').read_bytes().split(b'\0')
                need(any(arguments), 'Own application candidate argv is unavailable')
                desktop = desktop or any(x.rsplit(b'/', 1)[-1] == b'Lain.Desktop.dll' for x in arguments)
            result.append((int(p.name), exe, desktop))
        except (FileNotFoundError, ProcessLookupError): pass
        except PermissionError:
            if not candidate and recognized_unreadable_external(p, comm): continue
            raise RuntimeError('Cannot classify an unreadable own application candidate')
    return result


def no_desktop():
    need(not any(desktop for _, _, desktop in processes()), 'A Lain desktop is open; preserve its drafts and defer update')


def no_writers():
    no_desktop()
    need(not any(exe.startswith(str(STATE) + '/') for _, exe, _ in processes()), 'An installed own-state writer remains')


def project(items, keys):
    return sorted((tuple(json.dumps(x.get(k), sort_keys=True) for k in keys) for x in items), key=repr)


def baseline_identity(value):
    status = value['status']
    accounts = []
    for account in value['accounts']:
        item = {k: account.get(k) for k in ('id', 'name', 'provider', 'enabled', 'credentialKind', 'generation', 'binding', 'credentials')}
        item['selected'] = sorted((c['purpose'], c.get('credentialId')) for c in account.get('capabilities') or [])
        accounts.append(item)
    history = value['history']
    totals = history['totals']
    return {'status': {k: status.get(k) for k in ('revision', 'appliedRevision', 'runtimeMode', 'activeProfileId', 'activeProfileRevision')},
            'accounts': sorted(accounts, key=lambda a: a['id']),
            'profiles': project(value['profiles'], ('id', 'name', 'revision', 'routeIds', 'defaultRouteId')),
            'models': project(value['models']['models'], ('id', 'object', 'ownedBy')),
            'history': {'cursor': history['cursor'], 'totals': {k: totals.get(k) for k in ('requests', 'attempts', 'errors', 'unknownUsageAttempts')},
                        'tokens': {k: totals['tokens'].get(k) for k in ('input', 'output', 'cached', 'reasoning', 'total')}}}


def selected_paths(accounts, files):
    # Select via public DTO identity plus snapshot filenames only. Credential JSON
    # and SQLite are never opened or parsed by this script. Hashes remain private.
    selected = set()
    for account in accounts:
        account_id = account['id']
        need(re.fullmatch('[A-Za-z0-9_-]{1,100}', account_id) is not None, 'Unsupported public account identity')
        legacy = 'credentials/' + account_id + '.json'
        if legacy in files: selected.add(legacy)
        handles = account.get('credentials') or []
        ids = {cap.get('credentialId') for cap in account.get('capabilities') or [] if cap.get('credentialId')}
        for handle in handles:
            if handle['id'] not in ids: continue
            need(re.fullmatch('[A-Za-z0-9_-]{1,100}', handle['id']) is not None and
                 type(handle['generation']) is int and handle['generation'] > 0, 'Unsupported public credential handle')
            name = 'credentials/generations/' + handle['id'] + '/' + str(handle['generation']) + '.json'
            need(name in files, 'Selected credential generation absent from full snapshot')
            selected.add(name)
        # Material references are deliberately opaque; retain all material for
        # selected accounts instead of decoding private generation payloads.
        selected.update(name for name in files if name.startswith('credentials/material/' + account_id + '/'))
    selected.update(name for name in files if name.startswith(('proxy/auth/', 'proxy/auth-owned/')))
    return sorted(selected)


class Update:
    def __init__(self, args, verifier, plan):
        self.args, self.verifier, self.plan = args, verifier, plan
        self.backup = args.backup
        self.touched = False
        self.ui_opened = threading.Event()
        self.watch_done = threading.Event()
        self.watch_error = None
        self.env = dict(os.environ, LAIN_HOME=str(STATE), LAIN_SYSTEMD_DIR=str(CONFIG_HOME / 'systemd/user'),
                        LAIN_DESKTOP_DATA_DIR=str(PREFIX / 'share'), XDG_CONFIG_HOME=str(CONFIG_HOME),
                        XDG_DATA_HOME=str(PREFIX / 'share'))
        for name in tuple(self.env):
            if name.startswith('LAIN_REFERENCE_') or name == 'LAIN_PROXY_PATH': del self.env[name]
        self.record = {'format': 1, 'phase': 'prepared', 'oldSource': args.previous_source_commit, 'newSource': args.new_source_commit,
                       'service': 'lain.service', 'rollbackAttempted': False, 'plan': plan}

    def save(self): private_json(self.backup / 'update.json', self.record)

    def watch(self):
        while not self.watch_done.wait(.2):
            try: no_desktop()
            except Exception:
                self.watch_error = 'Desktop opened or process audit became unavailable'
                self.ui_opened.set(); return

    def guard(self):
        need(not self.ui_opened.is_set(), self.watch_error or 'Desktop opened during update')
        no_desktop()

    def run(self, command, timeout=120, allow_ui=False):
        if not allow_ui: self.guard()
        result = subprocess.run(list(map(str, command)), capture_output=True, env=self.env, cwd='/', timeout=timeout)
        if result.returncode:
            identity = Path(str(command[0])).name
            if re.fullmatch('[A-Za-z0-9._-]{1,80}', identity) is None: identity = 'unrecognized-command'
            with (self.backup / 'private-errors.log').open('a') as stream:
                stream.write(json.dumps({'command': identity, 'exitCode': result.returncode,
                                         'stdout': result.stdout.decode(errors='replace'),
                                         'stderr': result.stderr.decode(errors='replace')}) + '\n')
            raise RuntimeError('Own command failed: ' + identity + ' (exit ' + str(result.returncode) + ')')
        return result.stdout

    def cli(self, args): return json.loads(self.run([LAUNCHER, *args], timeout=30))
    def own_unit(self, require_running=False, allow_ui=False):
        output = self.run(['/usr/bin/systemctl', '--user', 'show', '--all', 'lain.service',
            '--property=LoadState', '--property=ActiveState', '--property=SubState',
            '--property=FragmentPath', '--property=ExecStart', '--property=MainPID',
            '--property=Environment', '--property=EnvironmentFiles', '--property=UnsetEnvironment',
            '--property=ExecStartPre', '--property=ExecStartPost', '--property=ExecStop', '--property=ExecStopPost'], allow_ui=allow_ui).decode()
        values = {}
        for line in output.splitlines():
            key, separator, value = line.partition('=')
            need(separator and key not in values, 'Malformed loaded own-unit metadata')
            values[key] = value
        need(values.get('LoadState') == 'loaded' and values.get('FragmentPath') == str(EXTERNAL[1]),
             'Loaded lain.service is not the exact own user unit')
        need(all(key in values for key in ('Environment', 'UnsetEnvironment')),
             'Required loaded own-state environment metadata is unavailable')
        if 'EnvironmentFiles' in values:
            need(not values['EnvironmentFiles'].strip(), 'Loaded environment files require separate review; update refused')
        else:
            # Some systemctl versions omit this empty property even with --all.
            # Missing output is never proof of absence: require its exact typed
            # empty-array response from the fixed own-unit D-Bus property.
            environment_files = self.run(['/usr/bin/busctl', '--user', 'get-property', 'org.freedesktop.systemd1',
                '/org/freedesktop/systemd1/unit/lain_2eservice', 'org.freedesktop.systemd1.Service',
                'EnvironmentFiles'], allow_ui=allow_ui).decode().strip()
            need(environment_files == 'a(sb) 0', 'Authoritative own-unit EnvironmentFiles is not an exact empty array')
        for hook in ('ExecStartPre', 'ExecStartPost', 'ExecStop', 'ExecStopPost'):
            if hook not in values:
                # This systemctl build also omits empty Exec arrays with --all.
                # Only the authoritative exact typed empty array proves absence.
                commands = self.run(['/usr/bin/busctl', '--user', 'get-property', 'org.freedesktop.systemd1',
                    '/org/freedesktop/systemd1/unit/lain_2eservice', 'org.freedesktop.systemd1.Service',
                    hook], allow_ui=allow_ui).decode().strip()
                need(commands == 'a(sasbttttuii) 0', 'Authoritative own-unit lifecycle hook is not an exact typed empty array')
                values[hook] = ''
        validate_unit_overrides(values)
        safe(EXTERNAL[1])
        need(EXTERNAL[1].is_file() and EXTERNAL[1].stat().st_uid == os.getuid(), 'Own unit file is not owned')
        start = values.get('ExecStart', '')
        need(start.count('{') == 1 and start.count('}') == 1 and start.startswith('{') and start.endswith('}'),
             'Expected one loaded own-unit ExecStart command')
        command = {}
        for item in start[1:-1].split(';'):
            if not item.strip(): continue
            key, separator, value = item.strip().partition('=')
            need(separator and key not in command, 'Malformed loaded ExecStart command')
            command[key] = value.strip()
        executable = str(STATE / 'app/host/Lain.Host')
        need(command.get('path') == executable and command.get('argv[]') == executable,
             'Loaded own-unit ExecStart must be only the exact own Host executable')
        need(re.fullmatch('[0-9]+', values.get('MainPID', '')) is not None, 'Invalid own-unit MainPID')
        pid = int(values['MainPID'])
        if require_running:
            need(values.get('ActiveState') == 'active' and values.get('SubState') == 'running' and pid > 0,
                 'Expected running own installed user unit')
        if pid:
            proc = Path('/proc') / str(pid)
            need(proc.stat().st_uid == os.getuid() and os.readlink(proc / 'exe') == executable,
                 'Loaded own-unit MainPID is not the exact installed Host process')
        # Environment values are parsed only in memory; never log/persist them.
        return {key: value for key, value in values.items()
                if key not in ('Environment', 'EnvironmentFiles', 'UnsetEnvironment')}

    def service(self, action, allow_ui=False):
        # Recheck even during error cleanup: never stop a remapped foreign unit.
        self.own_unit(allow_ui=allow_ui)
        self.run(['/usr/bin/systemctl', '--user', action, 'lain.service'], allow_ui=allow_ui)

    def capture(self, name):
        first = self.cli(['status', '--json'])
        commands = {'accounts': ['accounts', '--json'], 'models': ['models', '--json'], 'profiles': ['profiles', '--json'],
                    'history': ['history', '--from', self.args.history_from, '--to', self.args.history_to, '--timezone', 'UTC', '--json']}
        data = {'status': first}
        for key, command in commands.items(): data[key] = self.cli(command)
        last = self.cli(['status', '--json'])
        keys = ('version', 'revision', 'appliedRevision', 'activeProfileId', 'activeProfileRevision', 'runtimeMode')
        need(all(first.get(k) == last.get(k) for k in keys), 'Concurrent configuration change during CLI baseline')
        directory = self.backup / name; directory.mkdir(mode=0o700)
        for key, value in data.items(): private_json(directory / (key + '.json'), value)
        return data

    def check_selected(self, selected, files):
        for name in selected:
            need(sha(STATE / name) == files[name]['sha256'], 'Selected current credential/auth material changed; retain latest state for review')

