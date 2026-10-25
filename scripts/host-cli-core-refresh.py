#!/usr/bin/env python3
"""Plan/stage/install an exact Host+CLI+Core delta; never control services or data.

No operation runs payloads, builds, opens databases, reads credentials, or edits
external integration/Codex files. Both stage and install are dry-run by default.
"""
import argparse
import ctypes
import fcntl
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import tempfile

DESKTOP_SOURCE = '67fb26ce83f0f4aa2627fdde57d95ab8a1574355'
ITEMS = ['cli', 'host', 'proxy', 'notices/go', 'core-source-provenance',
         'core-refresh.py', 'core-refresh.json', 'host-cli-core-provenance',
         'host-cli-core-refresh.py', 'host-cli-core-refresh.json',
         'build-info.json', 'gate-status.json', 'SHA256SUMS']


def need(condition, message):
    if not condition:
        raise RuntimeError(message)


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def safe(path):
    path = Path(path)
    need(path.is_absolute() and '..' not in path.parts, 'Absolute nontraversing paths required')
    for item in (path, *path.parents):
        need(not item.is_symlink(), 'Symlink path refused')
    return path


def pin(path, expected):
    path = safe(path)
    need(re.fullmatch('[0-9a-f]{64}', expected) and sha(path) == expected, 'Input SHA256 pin differs')
    return path


def load_helper(expected):
    source = pin(Path(__file__).resolve().with_name('core-refresh.py'), expected)
    spec = importlib.util.spec_from_file_location('bounded_core_refresh', source)
    module = importlib.util.module_from_spec(spec)
    # Pure definitions/main guard only in the independently pinned helper.
    exec(compile(source.read_bytes(), str(source), 'exec'), module.__dict__)
    return module


def subset(files, prefix):
    return {name[len(prefix) + 1:]: value for name, value in files.items() if name.startswith(prefix + '/')}


def protected(base, target):
    owned = lambda name: any(name == item or name.startswith(item + '/') for item in ITEMS)
    need(all(owned(name) or target.get(name) == value for name, value in base.items()) and
         all(owned(name) or base.get(name) == value for name, value in target.items()),
         'Refresh changes a protected Desktop, notice, integration or package file')


def version_blob(value):
    raw = value.encode()
    need(len(raw) < 128, 'Informational version is too long for this bounded path')
    return b'\x01\x00' + bytes([len(raw)]) + raw + b'\x00\x00'


def external_graph(path):
    deps = json.loads(path.read_text())
    packages = {name: item for name, item in deps['libraries'].items() if item.get('type') != 'project'}
    return {'runtimeTarget': deps['runtimeTarget'], 'libraries': packages,
            'targets': {target: {name: item for name, item in values.items() if name in packages}
                        for target, values in deps['targets'].items()}}


def check_publish(common, base, component, record, commit):
    path = safe(record['path'])
    actual = common.inventory(path)
    need(actual == record['files'], component + ' publish differs from complete pinned inventory')
    previous = common.inventory(base / component)
    need(set(previous) == set(actual), 'Changed publish file set requires a separate dependency/notice update')
    project = 'Lain.' + ('Cli' if component == 'cli' else 'Host')
    allowed = {project, project + '.deps.json', project + '.dll', 'Lain.Core.dll', 'Lain.Client.dll'}
    allowed |= {name.removesuffix('.dll') + '.pdb' for name in tuple(allowed) if name.endswith('.dll')}
    need(all(actual[name] == old for name, old in previous.items() if name not in allowed),
         'External runtime/dependency payload changed; retained notices are insufficient')
    need(external_graph(path / (project + '.deps.json')) == external_graph(base / component / (project + '.deps.json')),
         'External dependency graph changed; retained notices are insufficient')
    version = record['informationalVersion']
    need(version.partition('+')[2] == commit, 'Publish version must identify the full frozen source commit')
    need(version_blob(version) in (path / (project + '.dll')).read_bytes(), 'Publish assembly version differs')
    need(os.access(path / project, os.X_OK), 'Publish apphost must be executable')
    return path, actual


def stage(args):
    inputs_path = pin(args.inputs, args.inputs_sha256)
    config = json.loads(inputs_path.read_text())
    need(config.get('format') == 1 and config.get('kind') == 'host-cli-core-refresh-inputs', 'Unsupported input manifest')
    commit = config['sourceCommit']
    need(re.fullmatch('[0-9a-f]{40}', commit), 'Full frozen source commit required')
    common = load_helper(config['helperSha256'])
    base = safe(config['base']['path'])
    core = safe(config['corePackage']['path'])
    pin(base / 'SHA256SUMS', config['base']['manifestSha256'])
    pin(core / 'SHA256SUMS', config['corePackage']['manifestSha256'])
    before, core_files = common.verify(base), common.verify(core)
    need(sha(core / 'core-refresh.py') == config['helperSha256'], 'Core package uses another helper version')
    core_delta = common.load(core / 'core-refresh.json')
    need(core_delta.get('kind') == 'schema2-core-only' and core_delta.get('baseFiles') == before,
         'Core package must be staged over this exact complete baseline')
    common.protected(before, core_files)
    common.check_core(core)
    info = common.load(base / 'build-info.json')
    need(info['componentSourceCommits']['desktop'] == DESKTOP_SOURCE, 'Unexpected retained Desktop source')
    for component, project in (('cli', 'Lain.Cli'), ('host', 'Lain.Host'), ('desktop', 'Lain.Desktop')):
        need(info['components'][component]['assemblySha256'] == sha(base / component / (project + '.dll')),
             'Baseline component provenance differs')
    publishes = {name: check_publish(common, base, name, config[name], commit) for name in ('cli', 'host')}
    evidence = safe(config['sourceEvidence']['path'])
    need(common.inventory(evidence) == config['sourceEvidence']['files'], 'Source evidence inventory differs')
    for name in publishes:
        lock = evidence / name / 'packages.lock.json'
        need(sha(lock) == info['components'][name]['lockfileSha256'], 'Changed source dependency lock requires a separate notice update')
        need((evidence / name / 'project.csproj').is_file(), 'Frozen project evidence missing')
        properties = common.load(evidence / name / 'publish-properties.json')
        need(properties.get('sourceCommit') == commit and properties.get('publishProperties', {}).get('InformationalVersion') == config[name]['informationalVersion']
             and properties['publishProperties'].get('RuntimeIdentifier') == 'linux-x64'
             and properties['publishProperties'].get('SelfContained') == 'true', 'Frozen publish properties differ')
    report_path = pin(config['qualification']['path'], config['qualification']['sha256'])
    report = common.load(report_path)
    expected_report = {'kind': 'host-cli-core-publish-qualification', 'passed': True, 'sourceCommit': commit,
                       'baseManifestSha256': before['SHA256SUMS'], 'coreManifestSha256': core_files['SHA256SUMS'],
                       'sourceEvidenceFiles': config['sourceEvidence']['files'],
                       'cliFiles': publishes['cli'][1], 'hostFiles': publishes['host'][1]}
    need(all(report.get(key) == value for key, value in expected_report.items()),
         'Qualification does not bind these exact baseline/Core/Host/CLI inputs')
    out = safe(config['output'])
    need(not out.exists(), 'Output must not exist')
    for source in (base, core, evidence, inputs_path, report_path, *(item[0] for item in publishes.values()), Path(__file__).resolve().parents[1]):
        need(out != source and out not in source.parents and source not in out.parents, 'Output overlaps input or checkout')
    plan = {'operation': 'stage', 'apply': args.apply, 'sourceCommit': commit,
            'inputManifestSha256': args.inputs_sha256, 'baseManifestSha256': before['SHA256SUMS'],
            'coreManifestSha256': core_files['SHA256SUMS'], 'desktopSourceCommit': DESKTOP_SOURCE,
            'desktopUnchanged': True, 'output': str(out), 'serviceRestartRequired': True}
    print(json.dumps(plan, indent=2))
    if not args.apply:
        return
    os.umask(0o077)
    shutil.copytree(core, out)
    for name, (publish, _) in publishes.items():
        shutil.rmtree(out / name)
        shutil.copytree(publish, out / name)
    provenance = out / 'host-cli-core-provenance'
    need(not provenance.exists(), 'Nested previous combined-refresh provenance is unsupported')
    provenance.mkdir()
    shutil.copyfile(inputs_path, provenance / 'inputs.json')
    shutil.copyfile(report_path, provenance / 'publish-qualification.json')
    shutil.copytree(evidence, provenance / 'source')
    for name in ('build-info.json', 'gate-status.json'):
        shutil.copyfile(base / name, provenance / ('baseline-' + name))
    current = common.load(out / 'build-info.json')
    for name, (publish, _) in publishes.items():
        project = 'Lain.' + ('Cli' if name == 'cli' else 'Host')
        record = dict(sourceCommit=commit, informationalVersion=config[name]['informationalVersion'],
                      assemblySha256=sha(publish / (project + '.dll')),
                      lockfileSha256=sha(evidence / name / 'packages.lock.json'),
                      localSharedAssemblies={p.stem: sha(p) for p in publish.glob('Lain.*.dll') if p.name != project + '.dll'})
        current['components'][name] = record
        current['componentSourceCommits'][name] = commit
        current['componentInformationalVersions'][name] = config[name]['informationalVersion']
    current['hostCliCoreRefresh'] = dict(plan, coreInputRefresh=current.pop('coreRefresh', None))
    common.write(out / 'build-info.json', current)
    gate = common.load(out / 'gate-status.json')
    gate.update(releaseQualified=False, status='awaiting-host-cli-core-installed-acceptance',
                currentRequirement='Exact staged package and installed retained state require separate acceptance; Desktop bytes retained.')
    common.write(out / 'gate-status.json', gate)
    shutil.copyfile(Path(__file__), out / 'host-cli-core-refresh.py')
    interim = common.inventory(out)
    protected(before, interim)
    need(subset(interim, 'desktop') == subset(before, 'desktop'), 'Desktop bytes changed')
    common.write(out / 'host-cli-core-refresh.json', {'format': 1, 'kind': 'host-cli-core-only',
                 'items': ITEMS, 'baseFiles': before, 'helperSha256': config['helperSha256'],
                 'installerSha256': sha(out / 'host-cli-core-refresh.py'), 'sourceCommit': commit,
                 'desktopSourceCommit': DESKTOP_SOURCE, 'inputManifestSha256': args.inputs_sha256})
    common.manifest(out)
    target = common.verify(out)
    protected(before, target)
    need(common.verify(base) == before and common.verify(core) == core_files, 'Baseline/Core input changed during stage')
    for name, (path, expected) in publishes.items():
        need(common.inventory(path) == expected and common.inventory(out / name) == expected, 'Publish changed during stage')
    pin(inputs_path, args.inputs_sha256)
    pin(report_path, config['qualification']['sha256'])
    need(common.inventory(evidence) == config['sourceEvidence']['files'], 'Source evidence changed during stage')
    print(json.dumps({'stagedManifestSha256': target['SHA256SUMS'], 'releaseQualified': False}))


def prepared_check(common, prepared, target):
    for name in ITEMS:
        path = prepared / name
        need(common.inventory(path) == subset(target, name) if path.is_dir() else sha(path) == target[name],
             'Prepared replacement differs from target')


def refuse_running_payloads(common, app):
    common.refuse_running_backend(app)
    targets = {str(app / name / ('Lain.' + project + suffix)) for name, project in
               (('cli', 'Cli'), ('desktop', 'Desktop')) for suffix in ('', '.dll')}
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit():
            continue
        try:
            if entry.stat().st_uid != os.getuid():
                continue
            executable = os.readlink(entry / 'exe').removesuffix(' (deleted)')
            arguments = (entry / 'cmdline').read_bytes().split(b'\0')
            if executable in targets or any(os.fsdecode(value) in targets for value in arguments):
                raise RuntimeError('Quiesce installed own CLI/Desktop before replacement; PID ' + entry.name)
        except (FileNotFoundError, PermissionError, ProcessLookupError):
            continue


def replace_items(common, app, prepared, backup, base, target):
    renameat2 = ctypes.CDLL(None, use_errno=True).renameat2
    renameat2.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]

    def exchange(left, right):
        if renameat2(-100, os.fsencode(left), -100, os.fsencode(right), 2):
            error = ctypes.get_errno()
            raise OSError(error, os.strerror(error))

    completed = []
    journal = {'kind': 'host-cli-core-file-transaction', 'status': 'prepared',
               'baseFiles': base, 'targetManifestSha256': target['SHA256SUMS'], 'items': ITEMS}
    common.write(backup / 'transaction.json', journal)
    try:
        for name in ITEMS:
            present = (app / name).exists()
            common.write(backup / 'transaction.json', dict(journal, status='replacing', completed=completed, activeItem=[name, present]))
            if present:
                exchange(app / name, prepared / name)
            else:
                (prepared / name).rename(app / name)
            completed.append((name, present))
            common.sync_directory((app / name).parent)
            common.sync_directory((prepared / name).parent)
            common.write(backup / 'transaction.json', dict(journal, status='replacing', completed=completed))
        need(common.verify(app) == target, 'Installed complete target differs')
        common.write(backup / 'transaction.json', dict(journal, status='installed-not-started', completed=completed))
    except BaseException:
        for name, present in reversed(completed):
            if present:
                exchange(app / name, prepared / name)
            else:
                (app / name).rename(prepared / name)
            common.sync_directory((app / name).parent)
            common.sync_directory((prepared / name).parent)
        need(common.verify(app) == base, 'File rollback differs; keep own service stopped and inspect transaction')
        common.write(backup / 'transaction.json', dict(journal, status='file-transaction-reversed', completed=completed))
        raise


def install(args):
    package, home, backup = safe(args.package), safe(args.home), safe(args.backup)
    pin(package / 'SHA256SUMS', args.manifest_sha256)
    delta = json.loads((package / 'host-cli-core-refresh.json').read_text())
    # Verify helper's digest against the externally pinned manifest before import.
    helper_line = f'{delta["helperSha256"]}  ./core-refresh.py'
    need(helper_line in (package / 'SHA256SUMS').read_text().splitlines(), 'Helper is not bound by pinned package manifest')
    need(Path(__file__).resolve().parent == package, 'Use the exact staged installer beside its helper')
    common = load_helper(delta['helperSha256'])
    target = common.verify(package)
    need(sha(Path(__file__)) == delta['installerSha256'] == target['host-cli-core-refresh.py'], 'Installer differs from staged artifact')
    need(delta.get('format') == 1 and delta.get('kind') == 'host-cli-core-only' and delta.get('items') == ITEMS,
         'Unsupported refresh manifest')
    app = home / 'app'
    need((app / '.lain-package').is_file(), 'Installed app is not managed')
    base = delta['baseFiles']
    need(common.verify(app) == base, 'Installed application differs from complete pinned baseline')
    protected(base, target)
    common.check_core(package)
    old, new = common.load(app / 'build-info.json'), common.load(package / 'build-info.json')
    for key in ('componentSourceCommits', 'componentInformationalVersions', 'components'):
        need(old[key]['desktop'] == new[key]['desktop'], 'Desktop provenance changed')
    need(new['componentSourceCommits']['desktop'] == DESKTOP_SOURCE and subset(base, 'desktop') == subset(target, 'desktop'), 'Desktop source/bytes changed')
    need(home in backup.parents and app not in backup.parents and backup != app and not backup.exists(), 'Fresh backup must be inside home and outside app')
    need(package != app and app not in package.parents and package not in app.parents and backup not in package.parents and package not in backup.parents, 'Package overlaps installation or backup')
    print(json.dumps({'operation': 'install', 'apply': args.apply, 'baseManifestSha256': base['SHA256SUMS'],
                      'targetManifestSha256': args.manifest_sha256, 'desktopUnchanged': True,
                      'serviceAction': 'none; root operator must stop/start only own lain', 'backup': str(backup)}, indent=2))
    if not args.apply:
        return
    need(args.stopped, 'Explicit --stopped acknowledgement is required for apply')
    os.umask(0o077)
    with (home / '.host-cli-core-refresh.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        refuse_running_payloads(common, app)
        backup.mkdir(parents=True, mode=0o700)
        need(backup.stat().st_dev == app.stat().st_dev, 'Backup and app must share filesystem')
        prepared = backup / 'previous'
        prepared.mkdir(mode=0o700)
        for name in ITEMS:
            destination = prepared / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            if (package / name).is_dir():
                shutil.copytree(package / name, destination)
            else:
                shutil.copy2(package / name, destination)
        need(common.verify(app) == base and common.verify(package) == target, 'Inputs changed before replacement')
        prepared_check(common, prepared, target)
        for path in prepared.rglob('*'):
            if path.is_file():
                with path.open('rb') as stream:
                    os.fsync(stream.fileno())
        for path in sorted((p for p in prepared.rglob('*') if p.is_dir()), key=lambda p: len(p.parts), reverse=True):
            common.sync_directory(path)
        common.sync_directory(prepared)
        common.sync_directory(backup)
        refuse_running_payloads(common, app)
        need(common.verify(app) == base and common.verify(package) == target, 'Complete baseline/target changed immediately before transaction')
        replace_items(common, app, prepared, backup, base, target)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='operation', required=True)
    staging = sub.add_parser('stage')
    staging.add_argument('--inputs', type=Path, required=True)
    staging.add_argument('--inputs-sha256', required=True)
    installing = sub.add_parser('install')
    for name in ('package', 'home', 'backup'):
        installing.add_argument('--' + name, type=Path, required=True)
    installing.add_argument('--manifest-sha256', required=True)
    installing.add_argument('--stopped', action='store_true')
    for command in (staging, installing):
        command.add_argument('--apply', action='store_true', help='explicit file mutations; otherwise read-only plan')
    args = parser.parse_args()
    try:
        (stage if args.operation == 'stage' else install)(args)
    except (OSError, RuntimeError, ValueError, KeyError) as error:
        parser.exit(2, f'Bounded refresh refused: {error}\n')


if __name__ == '__main__':
    main()
