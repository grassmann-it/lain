#!/usr/bin/env python3
"""Portable mixed Host/CLI/Core own-service update. Default is read-only plan.

Never builds, applies configuration, controls a foreign unit, opens/closes UI,
decodes credentials/SQLite, restores grants, or automatically rolls back state.
Execution requires separately reviewed exact-artifact synthetic qualification.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import http.client
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import selectors
import subprocess
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


def digest_prefix(path, length):
    value = hashlib.sha256()
    with Path(path).open('rb') as stream:
        while length:
            block = stream.read(min(length, 1024 * 1024))
            if not block: break
            value.update(block)
            length -= len(block)
    return value.hexdigest()


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


def verify_full_provenance(common, package, files, info, source):
    need(info.get('commit') == source and info.get('compileInputsFrozen') is True and
         info.get('runtime') == 'linux-x64' and info.get('runtimeMode') == 'gateway',
         'Full package must follow the frozen publish-linux Gateway contract')
    inventory_path = 'source-provenance/inventory.json'
    need(info['sourceProvenanceInventorySha256'] == files[inventory_path], 'Full source inventory hash differs')
    inventory = common.load(package / inventory_path)
    need(inventory.get('format') == 1 and inventory.get('sourceCommit') == source, 'Full source inventory identity differs')
    seen = set()
    for record in inventory['files']:
        name = record['path']
        need(isinstance(name, str) and name.startswith('source-provenance/') and
             '..' not in Path(name).parts and name not in seen and files.get(name) == record['sha256'],
             'Full source inventory entry differs')
        seen.add(name)
    for component, project in (('cli', 'Lain.Cli'), ('host', 'Lain.Host'), ('desktop', 'Lain.Desktop')):
        item = info['components'][component]
        version = info['componentInformationalVersions'][component]
        properties_path = 'source-provenance/publish-properties/' + component + '.json'
        lock_path = 'source-provenance/source/src/' + project + '/packages.lock.json'
        need(properties_path in seen and lock_path in seen and
             item['sourceCommit'] == info['componentSourceCommits'][component] == source and
             item['informationalVersion'] == version and
             version.partition('+')[2].split('.')[-1] == source and
             all(7 <= len(part) <= 40 and source.startswith(part) for part in version.partition('+')[2].split('.')) and
             item['assemblySha256'] == files[component + '/' + project + '.dll'] and
             item['lockfileSha256'] == files[lock_path], 'Full component provenance differs: ' + component)
        properties = common.load(package / properties_path)
        values = properties['publishProperties']
        need(properties['sourceCommit'] == source and properties['project'] == project and
             values['InformationalVersion'] == version and values['RuntimeIdentifier'] == 'linux-x64' and
             values['SelfContained'] == 'true', 'Full publish properties differ: ' + component)
        shared = {Path(name).stem: value for name, value in files.items()
                  if name.startswith(component + '/Lain.') and name.endswith('.dll') and
                  '/' not in name[len(component) + 1:] and name != component + '/' + project + '.dll'}
        need(item['localSharedAssemblies'] == shared, 'Full shared assembly provenance differs: ' + component)
    core_source = common.load(package / 'proxy/source-manifest.json')
    need(info['coreSourceCommit'] == source and
         files['core-source-provenance/proxy/source-manifest.json'] == files['proxy/source-manifest.json'],
         'Full Core source manifest provenance differs')
    for name, expected_hash in core_source['buildInputs'].items():
        need(not Path(name).is_absolute() and '..' not in Path(name).parts and
             files.get('core-source-provenance/' + name) == expected_hash, 'Full Core source input differs: ' + name)


def plan(config, fresh=True, installed="baseline"):
    """Read exact packages and evidence only; do not invoke payloads/services."""
    need(config['format'] == 1 and config['kind'] == 'portable-mixed-service-update-inputs', 'Unsupported operator inputs')
    source = config['sourceCommit']
    configure(config)
    need(os.environ.get('LAIN_HOME') != str(STATE),
         'Inherited LAIN_HOME selects installation; invoke with env -u LAIN_HOME before update')
    need(installed in ('baseline', 'target'), 'Explicit installed phase required')
    cutoff = datetime.fromisoformat(config['history']['to'].replace('Z', '+00:00'))
    if cutoff.tzinfo is None: cutoff = cutoff.replace(tzinfo=timezone.utc)
    need(cutoff <= datetime.now(timezone.utc), 'History end must be a fixed past UTC boundary before update')
    need(re.fullmatch('[0-9a-f]{40}', source) is not None, 'Full source commit required')
    package = safe(config['package']['path'])
    baseline = safe(config['baseline']['path'])
    pinned(package / 'SHA256SUMS', config['package']['manifestSha256'])
    pinned(baseline / 'SHA256SUMS', config['baseline']['manifestSha256'])
    variant = config.get('packageVariant', 'host-cli-core-retained-desktop')
    need(variant in ('host-cli-core-retained-desktop', 'full-components'), 'Unsupported package variant')
    retained = variant == 'host-cli-core-retained-desktop'
    helper_path = package / 'core-refresh.py' if retained else safe(config['coreHelperPath'])
    common = module(helper_path, config['coreHelperSha256'], 'exact_core_helper')
    before, after = common.verify(baseline), common.verify(package)
    need(common.verify(STATE / 'app') == (before if installed == 'baseline' else after),
         'Installed app differs from explicit complete ' + installed)
    old_info, new_info = common.load(baseline / 'build-info.json'), common.load(package / 'build-info.json')
    need(old_info['componentInformationalVersions']['host'] == config['previousHostVersion'],
         'Previous Host version must match exact baseline component provenance')
    if retained:
        installer = module(package / 'host-cli-core-refresh.py', config['installerSha256'], 'exact_mixed_installer')
        need(after['host-cli-core-refresh.py'] == config['installerSha256'] and
             after['core-refresh.py'] == config['coreHelperSha256'], 'Helpers absent from exact manifest')
        delta = common.load(package / 'host-cli-core-refresh.json')
        need(delta['format'] == 1 and delta['kind'] == 'host-cli-core-only' and
             delta['baseFiles'] == before and delta['items'] == installer.ITEMS and
             delta['helperSha256'] == config['coreHelperSha256'] and
             delta['installerSha256'] == config['installerSha256'] and delta['sourceCommit'] == source,
             'Mixed delta is not bound to exact baseline/source/helpers')
        installer.protected(before, after)
        for field in ('components', 'componentSourceCommits', 'componentInformationalVersions'):
            need(old_info[field]['desktop'] == new_info[field]['desktop'], 'Desktop metadata changed')
        need(new_info['componentSourceCommits']['desktop'] == DESKTOP and
             installer.subset(before, 'desktop') == installer.subset(after, 'desktop'), 'Desktop bytes/source changed')
    else:
        pinned(safe(config['installerPath']), config['installerSha256'])
        need(new_info['componentSourceCommits']['desktop'] == DESKTOP, 'Target Desktop source differs')
        need(DESKTOP == source, 'Fresh full package must use the explicit frozen application source for Desktop')
        need(new_info['componentInformationalVersions'] == config['componentInformationalVersions'],
             'Explicit full component informational versions differ')
        verify_full_provenance(common, package, after, new_info, source)
    common.check_core(package)
    for component in ('cli', 'host'):
        need(new_info['componentSourceCommits'][component] == source and
             new_info['componentInformationalVersions'][component] == ('1.0.0+' + source if retained else config['componentInformationalVersions'][component]),
             'Host/CLI source or version differs')
    core = config['core']
    need(after['proxy/lain-proxy'] == core['sha256'], 'Exact staged Go Core differs')
    need(re.fullmatch('[0-9a-f]{40}', core['sourceCommit']) is not None, 'Full Core source pin required')
    core_source = (common.load(package / 'core-source-provenance/inventory.json')['sourceCommit']
                   if retained else new_info['coreSourceCommit'])
    need(core_source == core['sourceCommit'], 'Core source provenance differs from supplied source pin')
    # Core provenance schema is separately bound by exact-artifact qualification.
    backup, app_backup = safe(config['backup']), safe(config['appDeltaBackup'])
    need((not fresh or not backup.exists()) and backup.parent.is_dir() and STATE not in backup.parents and
         backup != STATE and backup not in STATE.parents, 'Fresh outer full-backup destination required')
    need(STATE in app_backup.parents and STATE / 'app' not in app_backup.parents and
         app_backup != STATE / 'app' and app_backup.parent.is_dir() and (not fresh or not app_backup.exists()), 'Fresh app delta backup must be inside home outside app')
    for left in (backup, app_backup, STATE / 'app'):
        for right in (package, baseline):
            need(left != right and left not in right.parents and right not in left.parents, 'Input/backup/install overlap refused')
    expected = {'kind': 'portable-mixed-service-update-qualification', 'format': 1,
                'status': 'passed', 'planOnly': False, 'syntheticOnly': True,
                'sourceCommit': source, 'desktopSourceCommit': DESKTOP,
                'baselineManifestSha256': before['SHA256SUMS'], 'packageManifestSha256': after['SHA256SUMS'],
                'installerSha256': config['installerSha256'], 'coreHelperSha256': config['coreHelperSha256'],
                'core': core, 'preservationBaseline': config['preservationBaseline'],
                'operatorSha256': config['operatorSha256'], 'operatorHelpers': config['operatorHelpers'],
                'history': config['history']}
    if not retained:
        expected.update(kind='portable-full-service-update-qualification', packageVariant=variant,
                        componentInformationalVersions=config['componentInformationalVersions'])
    evidence = pinned(config['qualification']['path'], config['qualification']['sha256'])
    report = common.load(evidence)
    need(all(report.get(key) == value for key, value in expected.items()), 'Exact synthetic mixed qualification missing')
    required = ('exactArtifactTests', 'mixedSourceProvenance', 'retainedDesktopBytes', 'serviceFreeInstaller',
                'appProcessRefusal', 'fileTransactionFailureRecovery', 'stoppedBackup', 'syntheticStatePreservation',
                'quietRequestDrain', 'ownServiceIdentity', 'mainCodexPreservation')
    if not retained:
        required = tuple(key for key in required if key not in ('mixedSourceProvenance', 'retainedDesktopBytes')) + (
            'fullComponentSourceProvenance', 'desktopReplacement')
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
            'qualificationSha256': config['qualification']['sha256'], 'retainedDesktopBytes': retained, 'packageVariant': variant,
            'preservationBaseline': preservation}, common, before, after


def process_identity(proc):
    """Linux UID and starttime, never comm/argv as executable evidence."""
    uid = proc.stat().st_uid
    value = (proc / 'stat').read_text()
    fields = value[value.rindex(')') + 2:].split()
    need(len(fields) > 19 and fields[19].isdigit(), 'Invalid process starttime')
    return uid, int(fields[19])


def privileged_process_executable(proc):
    """Read only exe metadata using an already authenticated terminal sudo ticket."""
    message = 'Privileged process inspection failed; authenticate with sudo -v in the same terminal first'
    try:
        need(re.fullmatch(r'[0-9]+', proc.name) is not None, message)
        before = process_identity(proc)
        need(before[0] == os.getuid(), message)
        # Account for spawn time too; Popen itself has no portable spawn timeout.
        deadline = time.monotonic() + 3
        # No shell, password input, inherited output or executable from configuration.
        child = subprocess.Popen(['/usr/bin/sudo', '-n', '--', '/usr/bin/readlink', '--',
                                  '/proc/' + proc.name + '/exe'], stdin=subprocess.DEVNULL,
                                 stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                 env={'PATH': '/usr/bin:/bin', 'LC_ALL': 'C'})
        try:
            output = bytearray()
            with selectors.DefaultSelector() as selector:
                selector.register(child.stdout, selectors.EVENT_READ)
                while True:
                    remaining = deadline - time.monotonic()
                    need(remaining > 0 and selector.select(remaining), message)
                    block = os.read(child.stdout.fileno(), 4097 - len(output))
                    if not block: break
                    output.extend(block)
                    need(len(output) <= 4096, message)
            remaining = deadline - time.monotonic()
            need(remaining > 0, message)
            need(child.wait(timeout=remaining) == 0, message)
        finally:
            # Never use Popen's context manager: its exit waits without a timeout.
            try:
                if child.poll() is None:
                    child.kill()
                    child.wait(timeout=0.25)
            finally:
                child.stdout.close()
        need(process_identity(proc) == before, 'Process identity changed during privileged inspection')
        need(output.startswith(b'/') and output.endswith(b'\n') and
             b'\n' not in output[:-1] and b'\0' not in output, message)
        return os.fsdecode(bytes(output[:-1])).removesuffix(' (deleted)')
    except (OSError, ValueError, subprocess.TimeoutExpired):
        raise RuntimeError(message) from None


def prepare_update_class(args, config, expected, common, before_files, after_files):
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
            inspected = False
            try:
                if proc.stat().st_uid != os.getuid(): continue
                comm = (proc / 'comm').read_text().strip()
                try: executable = os.readlink(proc / 'exe').removesuffix(' (deleted)')
                except PermissionError:
                    if args.privileged_process_inspection:
                        inspected = True
                        identity = process_identity(proc)
                        comm_before = (proc / 'comm').read_text()
                        argv_before = (proc / 'cmdline').read_bytes()
                        comm = comm_before.strip()
                        executable = privileged_process_executable(proc)
                    else:
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
                if inspected:
                    # Repeated snapshots detect observed exec/metadata changes, not
                    # atomic quiescence: exec-and-restore between reads is possible.
                    executable_after = privileged_process_executable(proc)
                    comm_after = (proc / 'comm').read_text()
                    argv_after = (proc / 'cmdline').read_bytes()
                    need(process_identity(proc) == identity and
                         comm_after == comm_before and argv_after == argv_before and
                         arguments == tuple(os.fsdecode(x) for x in argv_before.split(b'\0') if x) and
                         executable_after == executable,
                         'Process metadata changed during privileged classification')
                # Existing no_writers consumes the executable field; expose a
                # canonical installed marker also for dotnet-hosted app DLLs.
                result.append((int(proc.name), str(STATE / 'app/observed-process') if installed else executable, desktop))
            except (FileNotFoundError, ProcessLookupError):
                if inspected:
                    raise RuntimeError('Process disappeared during privileged classification') from None
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
            self.record.update(format=2, kind='portable-component-service-update', newSource=config['sourceCommit'],
                               packageVariant=config.get('packageVariant', 'host-cli-core-retained-desktop'),
                               qualificationKind='synthetic-exact-package-only')
            self.drain_attempted = False
            self.drain_identity = None
            self.drain_files = before_files
            self.start_attempted = False

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
            journal = STATE / 'proxy/events.jsonl'
            self.journal_size = journal.stat().st_size
            return values

        def unchanged(self, values):
            def fingerprint(path):
                path = Path(path)
                if path == STATE / 'proxy/events.jsonl':
                    return digest_prefix(path, self.journal_size)
                return old.sha(path) if path.exists() else None
            need(all(fingerprint(path) == value for path, value in values.items()),
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

        def drain(self, files=before_files):
            self.drain_files = files
            self.drain_identity = self.backend_identity(files=files)
            token = self.management_token()
            initial = self.activity_request(token, 'GET', '/lain/activity', 3)
            need(initial['draining'] is False and self.backend_identity(files=self.drain_files) == self.drain_identity,
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
            need(self.backend_identity(files=self.drain_files) == self.drain_identity, 'Own backend changed during drain')

        def restore_admission(self):
            # Never stop an undrained service on failure. DELETE may succeed even
            # if its response is lost; independent GET must prove admission open.
            need(self.backend_identity(files=self.drain_files, allow_ui=True) == self.drain_identity, 'Cannot restore admission on changed backend')
            token = self.management_token()
            try: self.activity_request(token, 'DELETE', '/lain/drain', 3)
            except Exception: pass
            value = self.activity_request(token, 'GET', '/lain/activity', 3)
            need(value['draining'] is False and self.backend_identity(files=self.drain_files, allow_ui=True) == self.drain_identity,
                 'Own admission restoration unconfirmed; retain running streams for recovery')
            self.drain_attempted = False

        def confirm_drain(self):
            need(self.backend_identity(files=self.drain_files) == self.drain_identity, 'Own backend changed before service stop')
            value = self.activity_request(self.management_token(), 'GET', '/lain/activity', 3)
            need(value['draining'] is True and value['activeRequests'] == 0 and
                 self.backend_identity(files=self.drain_files) == self.drain_identity, 'Own request barrier not quiet immediately before stop')

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
                if config.get('packageVariant', 'host-cli-core-retained-desktop') == 'full-components':
                    command = ['/usr/bin/python3', config['installerPath'], '--package', config['package']['path'],
                               '--manifest-sha256', config['package']['manifestSha256'],
                               '--baseline-manifest-sha256', config['baseline']['manifestSha256'],
                               '--home', STATE, '--backup', config['appDeltaBackup'], '--stopped', '--apply',
                               '--helper', config['coreHelperPath'], '--helper-sha256', config['coreHelperSha256']]
                else:
                    command = ['/usr/bin/python3', Path(config['package']['path']) / 'host-cli-core-refresh.py', 'install',
                               '--package', config['package']['path'], '--manifest-sha256', config['package']['manifestSha256'],
                               '--home', STATE, '--backup', config['appDeltaBackup'], '--stopped', '--apply']
                self.run(command, timeout=300)
                self.require_app(after_files); self.unchanged(frozen); self.check_selected(selected, files)
                need(all((old.sha(Path(path)) if Path(path).exists() else None) == value for path, value in external.items()),
                     'External launcher/unit/desktop integration changed')
                self.record['phase'] = 'installed-not-started'; self.save()
                self.guard(); old.no_writers()
                self.drain_attempted = False
                self.drain_identity = None
                self.start_attempted = True  # A lost start response can still admit traffic.
                self.service('start')  # Exact unchanged unit; no daemon-reload/install integration.
                ready = False
                for _ in range(90):
                    self.guard()
                    try:
                        status = self.cli(['status', '--json'])
                        ready = status.get('version') == config.get('componentInformationalVersions', {}).get('host', '1.0.0+' + config['sourceCommit']) and status.get('proxyReady') is True
                        if ready: break
                    except (RuntimeError, json.JSONDecodeError): pass
                    time.sleep(.5)
                need(ready, 'New own Host/Core readiness failed')
                self.drain(files=after_files)
                self.confirm_drain()
                self.record['loadedOwnUnitAfter'] = self.own_unit(require_running=True)
                self.record['runningBackendAfter'] = self.backend_identity(files=after_files)
                after = self.capture('cli-after')
                after_identity = old.baseline_identity(after)
                # History query ends before update. New admitted requests may
                # advance its global cursor without changing that closed window.
                need(after_identity['history']['cursor'] >= identity['history']['cursor'], 'History cursor regressed')
                compared_identity = dict(after_identity, history=dict(after_identity['history'], cursor=identity['history']['cursor']))
                require_identity(identity, compared_identity, self.backup, 'Captured revision/account/profile/models/history identity changed')
                self.unchanged(frozen); self.check_selected(selected, files); self.require_app(after_files)
                need(all((old.sha(Path(path)) if Path(path).exists() else None) == value for path, value in external.items()),
                     'External integration changed after startup')
                need(plan(config, fresh=False, installed='target')[0] == expected, 'Package/evidence changed after update')
                self.guard()
                self.record.update(phase='verified', installed=True, fullStoppedSnapshotRetained=True,
                                   selectedCurrentCredentialsUnchanged=True, frozenMainAndSeparateLegacyUnchanged=True,
                                   revision=after['status']['revision'], appliedRevision=after['status']['appliedRevision'])
                self.restore_admission()
                self.record['admissionRestored'] = True
                self.save()
                print(json.dumps({'phase': 'verified', 'privateBackup': str(self.backup), 'rollbackAttempted': False}))
            except BaseException as error:
                stopped = None; failed = None
                if self.drain_attempted and not self.touched:
                    try: self.restore_admission(); self.record['admissionRestored'] = True
                    except BaseException: self.record['admissionRestored'] = False
                if self.touched:
                    try:
                        if self.start_attempted:
                            # Never reuse the predecessor's drain for a new Core.
                            if not self.drain_attempted or self.drain_files != after_files:
                                self.drain(files=after_files)
                            self.confirm_drain()
                        self.service('stop', allow_ui=True)
                        stopped = True
                    except BaseException as cleanup_error:
                        stopped = False
                        self.record['cleanupError'] = str(cleanup_error)
                        if self.start_attempted and self.drain_attempted:
                            try: self.restore_admission(); self.record['admissionRestored'] = True
                            except BaseException: self.record['admissionRestored'] = False
                if stopped:
                    try:
                        old.no_writers()
                        failed = self.backup / 'failed-current-stopped'
                        self.run(['/usr/bin/python3', helper, 'backup', STATE, failed, '--stopped'], timeout=300, allow_ui=True)
                    except BaseException: failed = None
                self.record.update(phase='failed-service-off' if stopped else 'failed-before-stop' if not self.touched else 'failed-poststart-recovery-required' if self.start_attempted else 'failed-stop-unconfirmed',
                                   errorType=type(error).__name__, errorMessage=str(error), serviceStopConfirmed=stopped,
                                   failedStoppedSnapshot=str(failed) if failed else None,
                                   currentStateRetainedAt=str(STATE), rollbackAttempted=False)
                try: self.save()
                except BaseException as evidence_error:
                    raise RuntimeError('Update failed: ' + str(error) + '; evidence write also failed: ' + str(evidence_error)) from error
                raise RuntimeError('Update failed: ' + str(error) + '; inspect private update.json; current state retained without automatic rollback') from error
            finally:
                self.watch_done.set(); watcher.join(timeout=2)

    return Update, old


def run_update(args, config, expected, common, before_files, after_files):
    Update, old = prepare_update_class(args, config, expected, common, before_files, after_files)
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
    parser.add_argument('--privileged-process-inspection', action='store_true',
                        help='Read inaccessible exe metadata via noninteractive sudo; run sudo -v in this terminal first')
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
