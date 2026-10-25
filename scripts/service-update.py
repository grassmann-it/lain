#!/usr/bin/env python3
"""Portable mixed Host/CLI/Core own-service update. Default is read-only plan.

Never builds, applies configuration, controls a foreign unit, opens/closes UI,
decodes credentials/SQLite, restores grants, or automatically rolls back state.
Execution requires separately reviewed exact-artifact synthetic qualification.
"""
import argparse
import hashlib
import http.client
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import threading
import time

HERE = Path(__file__).resolve().parent
STATE = None
DESKTOP = None


def need(value, message):
    if not value:
        raise RuntimeError(message)


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def safe(path):
    path = Path(path)
    need(path.is_absolute() and '..' not in path.parts, 'Absolute nontraversing paths required')
    for part in (path, *path.parents):
        need(not part.is_symlink(), 'Symlink path refused')
    return path


def pinned(path, expected):
    path = safe(path)
    need(re.fullmatch('[0-9a-f]{64}', expected) is not None and digest(path) == expected, 'Input pin differs')
    need(path.stat().st_uid == os.getuid(), 'Pinned file must be owned')
    return path


def module(path, expected, name):
    path = pinned(path, expected)
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    exec(compile(path.read_bytes(), str(path), 'exec'), result.__dict__)
    return result


def require_identity(before, after, evidence_root, label):
    """Bounded adaptation of the preserved V4 diagnostic; never persist raw values."""
    if before == after:
        return
    def fingerprint(value):
        return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()
    changes = {key: {'beforeSha256': fingerprint(before.get(key)),
                     'afterSha256': fingerprint(after.get(key))}
               for key in sorted(set(before) | set(after)) if before.get(key) != after.get(key)}
    path = safe(evidence_root) / 'protected-state-delta.json'
    with path.open('x') as stream:
        json.dump({'format': 1, 'label': label, 'changed': changes}, stream, indent=2)
    raise RuntimeError(label + '; hashed diagnostic: protected-state-delta.json')


def configure(config):
    global STATE, DESKTOP
    pinned(HERE / 'service-update.py', config['operatorSha256'])
    install = config['installation']
    STATE = safe(install['home'])
    DESKTOP = config['desktopSourceCommit']
    need(re.fullmatch('[0-9a-f]{40}', DESKTOP), 'Full retained Desktop source required')
    for name in ('prefix', 'configHome', 'launcher', 'codexHome'):
        safe(install[name])
    external = [safe(path) for path in install['externalFiles']]
    need(len(external) == len(set(external)) and len(external) >= 2 and
         external[0] == safe(install['launcher']) and
         external[1] == safe(install['configHome']) / 'systemd/user/lain.service',
         'Explicit ordered launcher/unit/external integration paths required')
    pinned(install['launcher'], install['launcherSha256'])
    pinned(external[1], install['unitSha256'])
    need(all(path != STATE and STATE not in path.parents for path in external), 'External integration must be outside state')
    need(install['managementPort'] == 18417, 'Only qualified own Core management port 18417 is supported')
    for name, filename in (('primitivesSha256', 'service-update-primitives.py'),
                           ('platformSha256', 'service-update-platform.py')):
        pinned(HERE / filename, config['operatorHelpers'][name])


def plan(config, fresh=True):
    """Read exact packages and evidence only; do not invoke payloads/services."""
    need(config['format'] == 1 and config['kind'] == 'portable-mixed-service-update-inputs', 'Unsupported operator inputs')
    source = config['sourceCommit']
    configure(config)
    need(re.fullmatch('[0-9a-f]{40}', source) is not None, 'Full source commit required')
    package = safe(config['package']['path'])
    baseline = safe(config['baseline']['path'])
    pinned(package / 'SHA256SUMS', config['package']['manifestSha256'])
    pinned(baseline / 'SHA256SUMS', config['baseline']['manifestSha256'])
    # Both imports are externally pinned, never resolved through the checkout/PATH.
    installer = module(package / 'host-cli-core-refresh.py', config['installerSha256'], 'exact_mixed_installer')
    common = module(package / 'core-refresh.py', config['coreHelperSha256'], 'exact_core_helper')
    before, after = common.verify(baseline), common.verify(package)
    need(common.verify(STATE / 'app') == before, 'Installed app differs from explicit complete baseline')
    need(after['host-cli-core-refresh.py'] == config['installerSha256'] and
         after['core-refresh.py'] == config['coreHelperSha256'], 'Helpers absent from exact manifest')
    delta = common.load(package / 'host-cli-core-refresh.json')
    need(delta['format'] == 1 and delta['kind'] == 'host-cli-core-only' and
         delta['baseFiles'] == before and delta['items'] == installer.ITEMS and
         delta['helperSha256'] == config['coreHelperSha256'] and
         delta['installerSha256'] == config['installerSha256'] and delta['sourceCommit'] == source,
         'Mixed delta is not bound to exact baseline/source/helpers')
    installer.protected(before, after)
    common.check_core(package)
    old_info, new_info = common.load(baseline / 'build-info.json'), common.load(package / 'build-info.json')
    need(old_info['componentInformationalVersions']['host'] == config['previousHostVersion'],
         'Previous Host version must match exact baseline component provenance')
    for field in ('components', 'componentSourceCommits', 'componentInformationalVersions'):
        need(old_info[field]['desktop'] == new_info[field]['desktop'], 'Desktop metadata changed')
    need(new_info['componentSourceCommits']['desktop'] == DESKTOP and
         installer.subset(before, 'desktop') == installer.subset(after, 'desktop'), 'Desktop bytes/source changed')
    for component in ('cli', 'host'):
        need(new_info['componentSourceCommits'][component] == source and
             new_info['componentInformationalVersions'][component] == '1.0.0+' + source,
             'Host/CLI source or version differs')
    core = config['core']
    need(after['proxy/lain-proxy'] == core['sha256'], 'Exact staged Go Core differs')
    need(re.fullmatch('[0-9a-f]{40}', core['sourceCommit']) is not None, 'Full Core source pin required')
    need(common.load(package / 'core-source-provenance/inventory.json')['sourceCommit'] == core['sourceCommit'],
         'Core source provenance differs from supplied source pin')
    # Core provenance schema is separately bound by exact-artifact qualification.
    backup, app_backup = safe(config['backup']), safe(config['appDeltaBackup'])
    need((not fresh or not backup.exists()) and backup.parent.is_dir() and STATE not in backup.parents and
         backup != STATE and backup not in STATE.parents, 'Fresh outer full-backup destination required')
    need(STATE in app_backup.parents and STATE / 'app' not in app_backup.parents and
         app_backup != STATE / 'app' and (not fresh or not app_backup.exists()), 'Fresh app delta backup must be inside home outside app')
    for left in (backup, app_backup, STATE / 'app'):
        for right in (package, baseline):
            need(left != right and left not in right.parents and right not in left.parents, 'Input/backup/install overlap refused')
    expected = {'kind': 'portable-mixed-service-update-qualification', 'format': 1,
                'status': 'passed', 'planOnly': False, 'syntheticOnly': True,
                'sourceCommit': source, 'desktopSourceCommit': DESKTOP,
                'baselineManifestSha256': before['SHA256SUMS'], 'packageManifestSha256': after['SHA256SUMS'],
                'installerSha256': config['installerSha256'], 'coreHelperSha256': config['coreHelperSha256'],
                'core': core, 'preservationBaseline': config['preservationBaseline'],
                'operatorSha256': config['operatorSha256'], 'operatorHelpers': config['operatorHelpers']}
    evidence = pinned(config['qualification']['path'], config['qualification']['sha256'])
    report = common.load(evidence)
    need(all(report.get(key) == value for key, value in expected.items()), 'Exact synthetic mixed qualification missing')
    required = ('exactArtifactTests', 'mixedSourceProvenance', 'retainedDesktopBytes', 'serviceFreeInstaller',
                'appProcessRefusal', 'fileTransactionFailureRecovery', 'stoppedBackup', 'syntheticStatePreservation',
                'quietRequestDrain', 'ownServiceIdentity', 'mainCodexPreservation')
    for key in required:
        item = report.get('checks', {}).get(key, {})
        need(item.get('passed') is True and isinstance(item.get('evidence'), str) and item['evidence'].strip(),
             'Missing qualified check: ' + key)
    preservation = config['preservationBaseline']
    need(all(type(preservation[key]) is int and preservation[key] >= 0 for key in
             ('accounts', 'routes', 'profiles', 'revision', 'appliedRevision')), 'Explicit nonnegative preservation counts required')
    need(re.fullmatch('[0-9a-f]{64}', preservation['configurationSha256']) is not None, 'Main config pin required')
    need(isinstance(config['legacyFingerprints'], list), 'Explicit legacy fingerprint list required (may be empty)')
    for item in config['legacyFingerprints']:
        safe(item['path'])
        need(re.fullmatch('[0-9a-f]{64}', item['sha256']) is not None, 'Legacy fingerprint pin required')
    main_codex = config['mainCodexFingerprints']
    need({str(safe(config['installation']['codexHome']) / name) for name in ('config.toml', 'auth.json')} <=
         {item['path'] for item in main_codex}, 'Explicit separate main Codex config/auth fingerprints required')
    need(len({item['path'] for item in main_codex}) == len(main_codex), 'Duplicate main Codex fingerprint')
    for item in main_codex:
        path = safe(item['path'])
        need(safe(config['installation']['codexHome']) in path.parents, 'Main Codex fingerprint outside exact Codex directory')
        need((item.get('missing') is True and 'sha256' not in item) or
             ('missing' not in item and re.fullmatch('[0-9a-f]{64}', item.get('sha256', '')) is not None),
             'Explicit main Codex SHA256 or missing semantics required')
    return {'sourceCommit': source, 'desktopSourceCommit': DESKTOP, 'core': core,
            'baselineManifestSha256': before['SHA256SUMS'], 'packageManifestSha256': after['SHA256SUMS'],
            'qualificationSha256': config['qualification']['sha256'], 'retainedDesktopBytes': True,
            'preservationBaseline': preservation}, common, before, after


def run_update(args, config, expected, common, before_files, after_files):
    old = module(HERE / 'service-update-primitives.py', config['operatorHelpers']['primitivesSha256'], 'portable_update_primitives')
    proof = module(HERE / 'service-update-platform.py', config['operatorHelpers']['platformSha256'], 'portable_platform_proof')
    install = config['installation']
    old.STATE, old.PREFIX = STATE, safe(install['prefix'])
    old.CONFIG_HOME, old.LAUNCHER = safe(install['configHome']), safe(install['launcher'])
    old.EXTERNAL = tuple(safe(path) for path in install['externalFiles'])
    old.LEASE = STATE / '.service-update.lock'
    # Replace only the old broad external-role exception with reviewed finite proof.
    old.recognized_unreadable_external = lambda proc, comm: proof.proves_platform_daemon(
        proc, (str(STATE) + '/app/',))

    def processes():
        result = []
        for proc in Path('/proc').iterdir():
            if not proc.name.isdigit() or int(proc.name) == os.getpid(): continue
            try:
                if proc.stat().st_uid != os.getuid(): continue
                comm = (proc / 'comm').read_text().strip()
                try: executable = os.readlink(proc / 'exe').removesuffix(' (deleted)')
                except PermissionError:
                    need(comm not in ('Lain.Host', 'Lain.Cli', 'Lain.Desktop', 'lain-proxy', 'dotnet') and
                         proof.proves_platform_daemon(proc, (str(STATE) + '/app/',)),
                         'Unreadable own process cannot be classified')
                    continue
                arguments = tuple(os.fsdecode(x) for x in (proc / 'cmdline').read_bytes().split(b'\0') if x)
                need(arguments or (comm not in ('Lain.Host', 'Lain.Cli', 'Lain.Desktop', 'lain-proxy', 'dotnet') and
                     not executable.startswith(str(STATE) + '/app/')), 'Application candidate argv unavailable')
                desktop = comm == 'Lain.Desktop' or Path(executable).name == 'Lain.Desktop' or any(
                    Path(x).name in ('Lain.Desktop', 'Lain.Desktop.dll') for x in arguments)
                installed = executable.startswith(str(STATE) + '/app/') or any(
                    x.startswith(str(STATE) + '/app/') for x in arguments)
                # Existing no_writers consumes the executable field; expose a
                # canonical installed marker also for dotnet-hosted app DLLs.
                result.append((int(proc.name), str(STATE / 'app/observed-process') if installed else executable, desktop))
            except (FileNotFoundError, ProcessLookupError): pass
            except PermissionError: raise RuntimeError('Own process metadata unreadable; defer update') from None
        return result

    old.processes = processes
    original_identity = old.baseline_identity

    def baseline_identity(value):
        identity = original_identity(value)
        models = []
        for model in value['models']['models']:
            item = {key: model.get(key) for key in ('id', 'provider', 'source')}
            item['accountIds'] = sorted(model['accountIds'])
            models.append(item)
        identity['models'] = sorted(models, key=lambda item: json.dumps(item, sort_keys=True))
        identity['profiles'] = sorted(value['profiles'], key=lambda item: item['id'])
        identity['history']['totals'] = value['history']['totals']
        # Preserve the actual nullable token DTO, including cacheCreation, and
        # public profile policy rather than silently projecting these away.
        identity['history']['tokens'] = value['history']['totals']['tokens']
        return identity

    old.baseline_identity = baseline_identity

    class Update(old.Update):
        def __init__(self):
            super().__init__(args, None, expected)
            self.record.update(format=2, kind='own-mixed-service-update', newSource=config['sourceCommit'],
                               qualificationKind='synthetic-mixed-only', allComponentsSameSourceV4=False)
            self.drain_attempted = False
            self.drain_identity = None

        def require_app(self, files):
            need(common.verify(STATE / 'app') == files, 'Installed app differs from exact complete manifest')

        def own_unit(self, require_running=False, allow_ui=False):
            value = super().own_unit(require_running=require_running, allow_ui=allow_ui)
            condition = self.run(['/usr/bin/busctl', '--user', 'get-property', 'org.freedesktop.systemd1',
                '/org/freedesktop/systemd1/unit/lain_2eservice', 'org.freedesktop.systemd1.Service',
                'ExecCondition'], allow_ui=allow_ui).decode().strip()
            need(condition == 'a(sasbttttuii) 0', 'Own-unit ExecCondition must be authoritatively exact empty')
            return value

        def frozen(self):
            values = {str(STATE / name): old.sha(STATE / name) for name in old.FROZEN}
            need(values[str(STATE / 'configuration.json')] == config['preservationBaseline']['configurationSha256'],
                 'Main configuration differs from reviewed retained baseline')
            for item in config['legacyFingerprints']:
                need(old.sha(Path(item['path'])) == item['sha256'], 'Separate legacy fingerprint differs')
                values[item['path']] = item['sha256']
            for item in config['mainCodexFingerprints']:
                path = safe(item['path'])
                expected_hash = None if item.get('missing') else item['sha256']
                need((old.sha(path) if path.exists() else None) == expected_hash, 'Separate main Codex fingerprint differs')
                values[str(path)] = expected_hash
            return values

        def unchanged(self, values):
            need(all((old.sha(Path(path)) if Path(path).exists() else None) == value for path, value in values.items()),
                 'Frozen main Codex/legacy/runtime fingerprint changed')

        def backend_identity(self, files=before_files, allow_ui=False):
            unit = self.own_unit(require_running=True, allow_ui=allow_ui)
            old.processes()  # Fail closed on unknown unreadable own processes.
            host_pid = int(unit['MainPID'])
            core_path = str(STATE / 'app/proxy/lain-proxy')
            cores = []
            for proc in Path('/proc').iterdir():
                if not proc.name.isdigit(): continue
                try:
                    if proc.stat().st_uid == os.getuid() and os.readlink(proc / 'exe') == core_path:
                        cores.append(int(proc.name))
                except (FileNotFoundError, ProcessLookupError, PermissionError): pass
            need(len(cores) == 1, 'Expected exactly one exact installed own Core')
            result = {}
            for role, pid, relative in (('host', host_pid, 'host/Lain.Host'), ('core', cores[0], 'proxy/lain-proxy')):
                proc = Path('/proc') / str(pid)
                raw = (proc / 'stat').read_text(); fields = raw.rsplit(')', 1)[1].split()
                need(proc.stat().st_uid == os.getuid() and fields[0] not in ('Z', 'X') and
                     os.readlink(proc / 'exe') == str(STATE / 'app' / relative), 'Backend process identity changed')
                actual = digest(proc / 'exe')  # Hash exact opened process executable, never argv/environment.
                need(actual == files[relative], 'Backend executable differs from pinned installed manifest')
                if role == 'core': need(int(fields[1]) == host_pid, 'Own Core is not a direct child of own Host')
                result[role] = {'pid': pid, 'start': int(fields[19]), 'sha256': actual}
                final = (proc / 'stat').read_text().rsplit(')', 1)[1].split()
                need((int(final[1]), int(final[19])) == (int(fields[1]), int(fields[19])) and
                     os.readlink(proc / 'exe') == str(STATE / 'app' / relative), 'Backend changed during identity read')
            # Bind the fixed management socket to the exact Core before sending
            # its private bearer, rather than trusting a loopback port alone.
            listeners = [line.split()[9] for line in Path('/proc/net/tcp').read_text().splitlines()[1:]
                         if line.split()[1] == '0100007F:47F1' and line.split()[3] == '0A']
            need(len(listeners) == 1, 'Expected exact loopback management listener')
            descriptors = Path('/proc') / str(cores[0]) / 'fd'
            sockets = set()
            for descriptor in descriptors.iterdir():
                try: sockets.add(os.readlink(descriptor))
                except FileNotFoundError: pass
            need('socket:[' + listeners[0] + ']' in sockets, 'Management listener is not owned by exact Core')
            return result

        def management_token(self):
            path = safe(STATE / 'credentials/management.token')
            info = path.stat()
            need(path.is_file() and info.st_uid == os.getuid() and not info.st_mode & 0o077 and info.st_size <= 4096,
                 'Owned private management token required')
            token = path.read_text().strip()
            need(token and not any(char.isspace() or ord(char) < 33 or ord(char) > 126 for char in token),
                 'Invalid local management token format')
            return token

        def activity_request(self, token, method, route, timeout):
            # HTTPConnection uses this fixed loopback socket directly; no proxy,
            # redirects, caller URL, provider credentials, or response logging.
            connection = http.client.HTTPConnection('127.0.0.1', 18417, timeout=timeout)
            try:
                connection.request(method, route, headers={'Authorization': 'Bearer ' + token})
                response = connection.getresponse()
                body = response.read(4097)
                need(200 <= response.status < 300 and len(body) <= 4096, 'Own Core management response refused')
                if route == '/lain/activity':
                    value = json.loads(body)
                    need(type(value.get('draining')) is bool and type(value.get('activeRequests')) is int and
                         value['activeRequests'] >= 0, 'Invalid own Core activity response')
                    return value
            finally:
                connection.close()

        def drain(self):
            self.drain_identity = self.backend_identity()
            token = self.management_token()
            initial = self.activity_request(token, 'GET', '/lain/activity', 3)
            need(initial['draining'] is False and self.backend_identity() == self.drain_identity,
                 'Existing or foreign admission barrier must not be adopted')
            deadline = time.monotonic() + 20
            self.drain_attempted = True  # POST response may be lost after admission closes.
            self.activity_request(token, 'POST', '/lain/drain', 3)
            while True:
                self.guard()
                remaining = deadline - time.monotonic()
                need(remaining > 0, 'Own request drain timed out; preserve existing streams')
                value = self.activity_request(token, 'GET', '/lain/activity', min(3, remaining))
                need(value['draining'], 'Own Core admission barrier disappeared')
                if value['activeRequests'] == 0: break
                time.sleep(min(.1, max(0, deadline - time.monotonic())))
            need(self.backend_identity() == self.drain_identity, 'Own backend changed during drain')

        def restore_admission(self):
            # Never stop an undrained service on failure. DELETE may succeed even
            # if its response is lost; independent GET must prove admission open.
            need(self.backend_identity(allow_ui=True) == self.drain_identity, 'Cannot restore admission on changed backend')
            token = self.management_token()
            try: self.activity_request(token, 'DELETE', '/lain/drain', 3)
            except Exception: pass
            value = self.activity_request(token, 'GET', '/lain/activity', 3)
            need(value['draining'] is False and self.backend_identity(allow_ui=True) == self.drain_identity,
                 'Own admission restoration unconfirmed; retain running streams for recovery')
            self.drain_attempted = False

        def confirm_drain(self):
            need(self.backend_identity() == self.drain_identity, 'Own backend changed before service stop')
            value = self.activity_request(self.management_token(), 'GET', '/lain/activity', 3)
            need(value['draining'] is True and value['activeRequests'] == 0 and
                 self.backend_identity() == self.drain_identity, 'Own request barrier not quiet immediately before stop')

        def execute(self):
            self.guard()
            need(not self.backup.exists(), 'Outer backup must remain fresh')
            self.backup.mkdir(mode=0o700)
            self.save()
            watcher = threading.Thread(target=self.watch, daemon=True); watcher.start()
            helper = Path(config['package']['path']) / 'packaging/lain-maintenance.py'
            try:
                self.record['loadedOwnUnitBefore'] = self.own_unit(require_running=True)
                self.drain()
                # Capture the final preserved history only after admitted
                # requests have completed behind our confirmed admission barrier.
                before = self.capture('cli-before')
                status = before['status']; identity = old.baseline_identity(before)
                p = config['preservationBaseline']
                need(status['version'] == config['previousHostVersion'] and status['runtimeMode'] == 'gateway' and
                     status.get('proxyReady') is True and status['revision'] == p['revision'] and
                     status['appliedRevision'] == p['appliedRevision'] and len(before['accounts']) == p['accounts'] and
                     len(before['profiles']) == p['profiles'], 'Expected stable running retained baseline')
                frozen = self.frozen()
                require_identity(identity, old.baseline_identity(self.capture('cli-prestop-check')), self.backup, 'Concurrent baseline change')
                self.require_app(before_files)
                external = {str(path): old.sha(path) if path.exists() else None for path in old.EXTERNAL}
                external_backup = self.backup / 'external'; external_backup.mkdir(mode=0o700)
                for number, path in enumerate(old.EXTERNAL):
                    if external[str(path)] is not None:
                        shutil.copy2(path, external_backup / str(number))
                        need(old.sha(external_backup / str(number)) == external[str(path)], 'External backup differs')
                self.record['externalFingerprints'] = external
                self.guard()
                self.confirm_drain()
                self.record['phase'] = 'stopping-own-service'; self.save()
                self.touched = True
                self.service('stop')
                old.no_writers(); self.unchanged(frozen)
                self.record['phase'] = 'own-service-stopped'; self.save()
                snapshot = self.backup / 'current-stopped'
                self.run(['/usr/bin/python3', helper, 'backup', STATE, snapshot, '--stopped'], timeout=300)
                manifest = json.loads((snapshot / 'snapshot.json').read_text())
                need(manifest.get('kind') == 'lain-stopped-state' and manifest.get('format') == 1, 'Unexpected full snapshot')
                files = manifest['files']
                need(all(files[name]['sha256'] == frozen[str(STATE / name)] for name in old.FROZEN), 'Full snapshot frozen files differ')
                need({name.removeprefix('app/'): item['sha256'] for name, item in files.items()
                      if name.startswith('app/') and name != 'app/.lain-package'} ==
                     before_files, 'Full snapshot does not contain exact complete installed baseline')
                selected = old.selected_paths(before['accounts'], files)
                self.check_selected(selected, files)
                need(plan(config, fresh=False)[0] == expected, 'Package/evidence changed before install')
                self.guard(); old.no_writers(); self.require_app(before_files)
                self.unchanged(frozen); self.check_selected(selected, files)
                need(all((old.sha(Path(path)) if Path(path).exists() else None) == value for path, value in external.items()),
                     'External integration changed before install')
                self.record['phase'] = 'installing-qualified-mixed-package'; self.save()
                self.run(['/usr/bin/python3', Path(config['package']['path']) / 'host-cli-core-refresh.py', 'install',
                          '--package', config['package']['path'], '--manifest-sha256', config['package']['manifestSha256'],
                          '--home', STATE, '--backup', config['appDeltaBackup'], '--stopped', '--apply'], timeout=300)
                self.require_app(after_files); self.unchanged(frozen); self.check_selected(selected, files)
                need(all((old.sha(Path(path)) if Path(path).exists() else None) == value for path, value in external.items()),
                     'External launcher/unit/desktop integration changed')
                self.record['phase'] = 'installed-not-started'; self.save()
                self.guard(); old.no_writers()
                self.service('start')  # Exact unchanged unit; no daemon-reload/install integration.
                ready = False
                for _ in range(90):
                    self.guard()
                    try:
                        status = self.cli(['status', '--json'])
                        ready = status.get('version') == '1.0.0+' + config['sourceCommit'] and status.get('proxyReady') is True
                        if ready: break
                    except (RuntimeError, json.JSONDecodeError): pass
                    time.sleep(.5)
                need(ready, 'New own Host/Core readiness failed')
                self.record['loadedOwnUnitAfter'] = self.own_unit(require_running=True)
                self.record['runningBackendAfter'] = self.backend_identity(files=after_files)
                after = self.capture('cli-after')
                require_identity(identity, old.baseline_identity(after), self.backup, 'Captured revision/account/profile/models/history identity changed')
                self.unchanged(frozen); self.check_selected(selected, files); self.require_app(after_files)
                need(all((old.sha(Path(path)) if Path(path).exists() else None) == value for path, value in external.items()),
                     'External integration changed after startup')
                need(plan(config, fresh=False)[0] == expected, 'Package/evidence changed after update')
                self.guard()
                self.record.update(phase='verified', installed=True, fullStoppedSnapshotRetained=True,
                                   selectedCurrentCredentialsUnchanged=True, frozenMainAndSeparateLegacyUnchanged=True,
                                   revision=after['status']['revision'], appliedRevision=after['status']['appliedRevision'])
                self.save()
                print(json.dumps({'phase': 'verified', 'privateBackup': str(self.backup), 'rollbackAttempted': False}))
            except BaseException as error:
                stopped = None; failed = None
                if self.drain_attempted and not self.touched:
                    try: self.restore_admission(); self.record['admissionRestored'] = True
                    except BaseException: self.record['admissionRestored'] = False
                if self.touched:
                    try: self.service('stop', allow_ui=True); stopped = True
                    except BaseException: stopped = False
                if stopped:
                    try:
                        old.no_writers()
                        failed = self.backup / 'failed-current-stopped'
                        self.run(['/usr/bin/python3', helper, 'backup', STATE, failed, '--stopped'], timeout=300, allow_ui=True)
                    except BaseException: failed = None
                self.record.update(phase='failed-service-off' if stopped else 'failed-before-stop' if not self.touched else 'failed-stop-unconfirmed',
                                   errorType=type(error).__name__, errorMessage=str(error), serviceStopConfirmed=stopped,
                                   failedStoppedSnapshot=str(failed) if failed else None,
                                   currentStateRetainedAt=str(STATE), rollbackAttempted=False)
                try: self.save()
                except BaseException as evidence_error:
                    raise RuntimeError('Update failed: ' + str(error) + '; evidence write also failed: ' + str(evidence_error)) from error
                raise RuntimeError('Update failed: ' + str(error) + '; inspect private update.json; current state retained without automatic rollback') from error
            finally:
                self.watch_done.set(); watcher.join(timeout=2)

    # One lease shared with previous v4/client operators; nested installer keeps
    # its own distinct app transaction lock. No service command precedes lease.
    old.no_desktop()
    with old.updater_lease():
        need(plan(config)[0] == expected, 'Inputs changed while acquiring shared lease')
        Update().execute()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--inputs', type=Path, required=True)
    parser.add_argument('--inputs-sha256', required=True)
    parser.add_argument('--execute', action='store_true')
    args = parser.parse_args()
    os.umask(0o077)
    inputs = pinned(args.inputs, args.inputs_sha256)
    config = json.loads(inputs.read_text())
    expected, common, before, after = plan(config)
    if not args.execute:
        print(json.dumps({'phase': 'planned', 'plan': expected, 'serviceAction': 'none', 'mutations': False}, indent=2))
        return
    # Compatibility fields for unchanged, pinned Update primitives only.
    args.backup = Path(config['backup'])
    args.previous_source_commit = config['previousHostVersion']
    args.new_source_commit = config['sourceCommit']
    args.history_from, args.history_to = config['history']['from'], config['history']['to']
    pinned(inputs, args.inputs_sha256)
    run_update(args, config, expected, common, before, after)


if __name__ == '__main__':
    try: main()
    except (RuntimeError, OSError, ValueError, KeyError) as error:
        print('Portable mixed update refused: ' + str(error), file=__import__('sys').stderr)
        raise SystemExit(2)
