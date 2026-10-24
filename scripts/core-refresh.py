#!/usr/bin/env python3
"""Stage/install a qualified schema-2 Core over an exact existing lain package.

Never builds, starts/stops services, runs application binaries or changes private
data. Existing Host/CLI/Desktop payloads and their notices retain their provenance.
"""
import argparse
import ctypes
import fcntl
import gzip
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile

ITEMS = ['proxy', 'notices/go', 'core-source-provenance', 'core-refresh.py', 'core-refresh.json',
         'build-info.json', 'gate-status.json', 'SHA256SUMS']


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(block)
    return value.hexdigest()


def load(path):
    return json.loads(path.read_text())


def sync_directory(path):
    descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, name = tempfile.mkstemp(prefix='.' + path.name + '-', dir=path.parent)
    temporary = Path(name)
    try:
        with os.fdopen(descriptor, 'w') as stream:
            stream.write(json.dumps(value, indent=2) + '\n')
            stream.flush()
            os.fsync(stream.fileno())
        temporary.replace(path)
        sync_directory(path.parent)
    finally:
        temporary.unlink(missing_ok=True)


def inventory(root):
    require(root.is_dir() and not root.is_symlink(), 'Expected regular directory: ' + str(root))
    result = {}
    for path in sorted(root.rglob('*')):
        require(not path.is_symlink(), 'Symlinks are unsupported: ' + str(path))
        require(path.is_file() or path.is_dir(), 'Special files are unsupported: ' + str(path))
        if path.is_file() and path.relative_to(root).as_posix() != '.lain-package':
            result[path.relative_to(root).as_posix()] = digest(path)
    return result


def verify(root):
    recorded = {}
    for line in (root / 'SHA256SUMS').read_text().splitlines():
        require(len(line) > 68 and line[64:68] == '  ./' and re.fullmatch('[0-9a-f]{64}', line[:64]), 'Invalid checksum line')
        name = line[68:]
        require(name and '\\' not in name and not name.startswith('/') and '..' not in Path(name).parts
                and name not in recorded and name not in ('SHA256SUMS', '.lain-package'), 'Unsafe/duplicate checksum path')
        recorded[name] = line[:64]
    actual = inventory(root)
    actual.pop('SHA256SUMS', None)
    require(actual == recorded, 'Package inventory differs from SHA256SUMS: ' + str(root))
    return inventory(root)


def manifest(root):
    entries = inventory(root)
    entries.pop('SHA256SUMS', None)
    (root / 'SHA256SUMS').write_text(''.join(f'{value}  ./{name}\n' for name, value in sorted(entries.items())))


def owned(name):
    return any(name == item or name.startswith(item + '/') for item in ITEMS)


def protected(base, target):
    require(all(owned(name) or target.get(name) == value for name, value in base.items()) and
            all(owned(name) or base.get(name) == value for name, value in target.items()),
            'Core refresh changes a protected application, notice or integration file')


def check_core(package):
    proxy = package / 'proxy'
    source, build, qualification = (load(proxy / name) for name in ('source-manifest.json', 'build-manifest.json', 'qualification.json'))
    require(source.get('schemaVersion') == 2, 'Core refresh requires source-manifest schema 2')
    upstream = source['upstreamDependency']
    require(build.get('binarySha256') == digest(proxy / 'lain-proxy') and
            build.get('sourceManifestSha256') == digest(proxy / 'source-manifest.json'), 'Core build evidence differs from payload')
    for key in ('Version', 'Commit', 'ArchiveSha256'):
        require(build.get('upstream' + key) == upstream[key[0].lower() + key[1:]], 'Upstream build evidence mismatch: ' + key)
    require(build.get('snapshotVersion') == source['snapshotVersion'] and build.get('lainPatchVersion') == source['lainPatchVersion']
            and build.get('patchSetSha256') == source['patchSetSha256'] and
            build.get('upstreamSourceModified') is bool(source['patches']) and
            source.get('upstreamSourceModified') is build.get('upstreamSourceModified'), 'Core patch provenance mismatch')
    require(digest(proxy / 'LICENSE') == source['licenseSha256'], 'Pinned upstream license differs')
    require(os.access(proxy / 'lain-proxy', os.X_OK), 'Core must be executable')
    require(qualification.get('passed') is True and qualification.get('sha256') == build['binarySha256']
            and qualification.get('sourceManifestSha256') == build['sourceManifestSha256'],
            'Independent qualification must pass for this exact binary and source manifest')
    require(load(proxy / 'toolchain.json')['goVersion'] in build['goVersion'].split(), 'Core toolchain differs from source pin')
    notices = load(package / 'notices/go/inventory.json')
    require(notices.get('binarySha256') == build['binarySha256'] and
            notices.get('sourceManifestSha256') == build['sourceManifestSha256'] and
            notices.get('upstreamDependency') == upstream and notices.get('patchSetSha256') == build['patchSetSha256'],
            'Go notice inventory differs from new Core')
    require(not notices.get('missingEvidence'), 'Go root notice evidence is incomplete; review before staging')
    require(any(entry.get('module') == upstream['module'] and
                entry.get('evidenceState') == 'verified-external-upstream-root-notices-collected'
                for entry in notices['modules']), 'Verified upstream license inventory missing')
    return source, build, qualification


def stage(args):
    root = Path(__file__).resolve().parents[1]
    git = lambda *parts: subprocess.check_output(['git', *parts], cwd=root)
    revision = git('rev-parse', args.source_commit + '^{commit}').decode().strip()
    source = lambda name: git('show', revision + ':' + name)
    frozen = ['proxy', 'scripts/core-refresh.py', 'scripts/collect-go-notices.py', 'scripts/prepare-core.py', 'scripts/build-core-verify.py']
    subprocess.run(['git', 'diff', '--exit-code', revision, '--', *frozen], cwd=root, check=True, stdout=subprocess.DEVNULL)
    require(not git('ls-files', '--others', '--exclude-standard', 'proxy').strip(), 'Untracked Core sources must be resolved')
    source_manifest = load(root / 'proxy/source-manifest.json')
    require(source_manifest.get('schemaVersion') == 2, 'Use source-manifest schema 2 for Core-only refresh')
    require(all((root / name).is_file() and digest(root / name) == sha for name, sha in source_manifest['buildInputs'].items()),
            'Frozen Core build-input inventory mismatch')
    base = args.base.resolve(strict=True)
    require(args.output.is_absolute(), 'Output must be absolute')
    out = args.output.resolve()
    archive, checksum = Path(str(out) + '.tar.gz'), Path(str(out) + '.tar.gz.sha256')
    require(not any(path.exists() or path.is_symlink() for path in (out, archive, checksum)), 'Output/archive/checksum must not exist')
    inputs = [root, base, args.core.resolve(strict=True), args.core_build_manifest.resolve(strict=True), args.qualification.resolve(strict=True)]
    require(not any(out == path or out in path.parents or path in out.parents for path in inputs), 'Output overlaps source/input')
    base_files = verify(base)
    info, gate = load(base / 'build-info.json'), load(base / 'gate-status.json')
    for component, project in [('cli', 'Lain.Cli'), ('host', 'Lain.Host'), ('desktop', 'Lain.Desktop')]:
        require(info['components'][component]['assemblySha256'] == digest(base / component / (project + '.dll')),
                'Retained component provenance differs: ' + component)
    binary_hash, build_bytes, qualification_bytes = digest(args.core), args.core_build_manifest.read_bytes(), args.qualification.read_bytes()
    # The baseline is copied wholesale; only the bounded owned paths change.
    shutil.copytree(base, out)
    for name in ('proxy', 'notices/go', 'core-source-provenance'):
        path = out / name
        if path.exists():
            shutil.rmtree(path)
    proxy = out / 'proxy'
    proxy.mkdir()
    shutil.copy2(args.core, proxy / 'lain-proxy')
    (proxy / 'build-manifest.json').write_bytes(build_bytes)
    (proxy / 'qualification.json').write_bytes(qualification_bytes)
    for name in ('source-manifest.json', 'upstream.json', 'toolchain.json'):
        (proxy / name).write_bytes(source('proxy/' + name))
    (proxy / 'LICENSE').write_bytes(source('proxy/LICENSE.upstream'))
    build = load(proxy / 'build-manifest.json')
    write(proxy / 'binary-manifest.json', {'format': 2, 'binary': 'lain-proxy', 'sha256': binary_hash,
          'sourceManifestSha256': digest(proxy / 'source-manifest.json'), 'upstreamVersion': build.get('upstreamVersion'),
          'upstreamCommit': build.get('upstreamCommit'), 'upstreamSourceModified': build.get('upstreamSourceModified'),
          'patchSetSha256': build.get('patchSetSha256')})
    # Private verified source assembly exists only for source/license evidence.
    # go list is readonly/offline; neither this step nor the collector builds.
    with tempfile.TemporaryDirectory(prefix='lain-core-notices-') as temporary:
        prepared = Path(temporary) / 'prepared'
        command = [sys.executable, str(root / 'scripts/prepare-core.py'), '--destination', str(prepared)]
        if args.archive:
            command.extend(['--archive', str(args.archive.resolve(strict=True))])
        assembled = json.loads(subprocess.check_output(command, cwd=root, text=True))
        adapter = Path(assembled['adapter'])
        env = dict(os.environ, GOTOOLCHAIN='local', GOWORK='off', GOPROXY='off', GOSUMDB='off')
        subprocess.run([sys.executable, str(root / 'scripts/collect-go-notices.py'), str(proxy / 'lain-proxy'), str(out / 'notices/go'),
                        '--prepared-adapter', str(adapter), '--source-manifest', str(proxy / 'source-manifest.json'),
                        '--build-manifest', str(proxy / 'build-manifest.json')], env=env, check=True)
    source_manifest, build, qualification = check_core(out)
    provenance = out / 'core-source-provenance'
    paths = sorted(set([*source_manifest['buildInputs'], 'proxy/source-manifest.json', 'scripts/prepare-core.py',
                        'scripts/build-core.sh', 'scripts/build-core-verify.py', 'scripts/collect-go-notices.py', 'scripts/core-refresh.py']))
    records = []
    for name in paths:
        require(not Path(name).is_absolute() and '..' not in Path(name).parts, 'Unsafe Core provenance source path')
        destination = provenance / 'source' / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(source(name))
        records.append({'sourcePath': name, 'path': str(destination.relative_to(out)), 'sha256': digest(destination)})
    write(provenance / 'inventory.json', {'format': 1, 'sourceCommit': revision, 'files': records,
          'scope': 'Own adapter, explicit patches, upstream archive lock, toolchain and packaging source; upstream source is external and is not vendored'})
    for name in ('build-info.json', 'gate-status.json'):
        shutil.copyfile(base / name, provenance / ('baseline-' + name))
    for name in ('build-manifest.json', 'source-manifest.json', 'qualification.json'):
        shutil.copyfile(base / 'proxy' / name, provenance / ('baseline-core-' + name))
    shutil.copy2(root / 'scripts/core-refresh.py', out / 'core-refresh.py')
    old_core = {key: info.get(key) for key in ('coreSourceCommit', 'proxySha256', 'sourceManifestSha256', 'coreBuildManifestSha256', 'qualificationSha256')}
    info.update(corePackagingCommit=revision, coreSourceCommit=revision, proxySha256=binary_hash,
                coreBuildManifestSha256=digest(proxy / 'build-manifest.json'), sourceManifestSha256=digest(proxy / 'source-manifest.json'),
                qualificationSha256=digest(proxy / 'qualification.json'), coreSourceProvenanceInventorySha256=digest(provenance / 'inventory.json'),
                coreRefresh={'sourceCommit': revision, 'baseManifestSha256': base_files['SHA256SUMS'],
                    'hostUnchanged': True, 'cliUnchanged': True, 'desktopUnchanged': True, 'applicationNoticesUnchanged': True,
                    'serviceRestartRequired': True, 'installerSha256': digest(out / 'core-refresh.py'), 'previousCore': old_core})
    write(out / 'build-info.json', info)
    # Retain exact old gates separately. None imply current package acceptance.
    new_gate = dict(gate)
    for key in list(new_gate):
        if key.endswith('Qualified'):
            new_gate[key] = False
    new_gate.update(backendQualified=True, releaseQualified=False, expansionQualified=False, coreRefreshQualified=False,
                    status='awaiting-independent-core-refresh-package-and-installed-acceptance',
                    proxySha256=binary_hash, sourceManifestSha256=digest(proxy / 'source-manifest.json'),
                    qualification={'path': 'proxy/qualification.json', 'sha256': digest(proxy / 'qualification.json'), 'scope': qualification.get('scope')},
                    previousCoreGate='core-source-provenance/baseline-gate-status.json',
                    retainedApplicationScope='Exact existing Host/CLI/Desktop payloads and their provenance/notices; no application rebuild or renewed live-provider qualification')
    write(out / 'gate-status.json', new_gate)
    write(out / 'core-refresh.json', {'format': 1, 'kind': 'schema2-core-only', 'installItems': ITEMS, 'sourceCommit': revision,
          'baseFiles': base_files, 'proxySha256': binary_hash, 'sourceManifestSha256': digest(proxy / 'source-manifest.json'),
          'baseManifestSha256': base_files['SHA256SUMS'], 'retainedComponentSourceCommits': info['componentSourceCommits']})
    require(verify(base) == base_files, 'Baseline changed during staging')
    require(digest(args.core) == binary_hash and args.core_build_manifest.read_bytes() == build_bytes and
            args.qualification.read_bytes() == qualification_bytes, 'Core/evidence input changed during staging')
    subprocess.run(['git', 'diff', '--exit-code', revision, '--', *frozen], cwd=root, check=True, stdout=subprocess.DEVNULL)
    require(not git('ls-files', '--others', '--exclude-standard', 'proxy').strip(), 'Core source changed during staging')
    manifest(out)
    target = verify(out)
    protected(base_files, target)
    epoch = int(os.environ.get('SOURCE_DATE_EPOCH') or git('show', '-s', '--format=%ct', revision).decode().strip())
    def normalized(member):
        member.uid = member.gid = 0
        member.uname = member.gname = ''
        member.mtime = epoch
        return member
    with archive.open('xb') as raw:
        with gzip.GzipFile(filename='', fileobj=raw, mode='wb', mtime=0, compresslevel=3) as compressed:
            with tarfile.open(fileobj=compressed, mode='w|') as tar:
                tar.add(out, arcname=out.name, filter=normalized)
    checksum.write_text(f'{digest(archive)}  {archive.name}\n')
    print('Staged qualified Core only; retained application payloads/notices unchanged, current release acceptance false. Nothing installed.')


def refuse_running_backend(app):
    targets = {str(app / 'proxy/lain-proxy'), str(app / 'host/Lain.Host'), str(app / 'host/Lain.Host.dll')}
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit():
            continue
        try:
            if entry.stat().st_uid != os.getuid():
                continue
            executable = os.readlink(entry / 'exe').removesuffix(' (deleted)')
            arguments = (entry / 'cmdline').read_bytes().split(b'\0')
            if executable in targets or any(os.fsdecode(value) in targets for value in arguments):
                raise RuntimeError('Stop the installed lain Host/Core before replacement; PID ' + entry.name)
        except (FileNotFoundError, PermissionError, ProcessLookupError):
            continue


def install(args):
    package, home = args.package.resolve(strict=True), args.home.resolve(strict=True)
    app, backup = home / 'app', args.backup
    require(backup.is_absolute() and not backup.exists() and not backup.is_symlink(), 'Backup must be absolute and nonexistent')
    backup = backup.resolve()
    require(home in backup.parents and app not in backup.parents and backup != app, 'Backup must be inside LAIN_HOME, outside app')
    require((app / '.lain-package').is_file() and not app.is_symlink(), 'Installed app must be managed and regular')
    require(package != app and app not in package.parents and package not in app.parents, 'Package overlaps app')
    require(package != backup and package not in backup.parents and backup not in package.parents, 'Backup overlaps package')
    with (home / '.core-refresh.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        target = verify(package)
        delta = load(package / 'core-refresh.json')
        require(delta.get('format') == 1 and delta.get('kind') == 'schema2-core-only' and delta.get('installItems') == ITEMS,
                'Unsupported Core refresh manifest')
        base = delta['baseFiles']
        require(verify(app) == base and delta.get('baseManifestSha256') == base['SHA256SUMS'], 'Installed files differ from exact refresh baseline')
        protected(base, target)
        require(digest(Path(__file__)) == target['core-refresh.py'], 'Run the exact staged Core installer')
        source, build, _ = check_core(package)
        require(delta.get('proxySha256') == build['binarySha256'] and delta.get('sourceManifestSha256') == build['sourceManifestSha256'],
                'Refresh delta differs from Core evidence')
        previous, current = load(app / 'build-info.json'), load(package / 'build-info.json')
        for key in ('componentSourceCommits', 'componentInformationalVersions', 'components'):
            require(previous[key] == current[key], 'Retained application provenance changed: ' + key)
        require(delta.get('retainedComponentSourceCommits') == previous['componentSourceCommits'], 'Retained component manifest differs')
        refuse_running_backend(app)
        renameat2 = ctypes.CDLL(None, use_errno=True).renameat2
        renameat2.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
        def exchange(left, right):
            if renameat2(-100, os.fsencode(left), -100, os.fsencode(right), 2):
                code = ctypes.get_errno()
                raise OSError(code, os.strerror(code))
        backup.mkdir(parents=True, mode=0o700)
        require(backup.stat().st_dev == app.stat().st_dev, 'Backup and app must share filesystem')
        prepared = backup / 'previous'
        prepared.mkdir(mode=0o700)
        for name in ITEMS:
            source_path, destination = package / name, prepared / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            if source_path.is_dir():
                shutil.copytree(source_path, destination)
            else:
                shutil.copy2(source_path, destination)
        require(verify(app) == base and verify(package) == target, 'Inputs changed while preparing update')
        for name in ITEMS:
            expected = {key[len(name) + 1:]: value for key, value in target.items() if key.startswith(name + '/')}
            require(inventory(prepared / name) == expected if (prepared / name).is_dir() else digest(prepared / name) == target[name],
                    'Prepared replacement differs from target: ' + name)
        # Persist copied replacements before the first exchange and journal.
        for path in prepared.rglob('*'):
            if path.is_file():
                with path.open('rb') as stream:
                    os.fsync(stream.fileno())
        for path in sorted((p for p in prepared.rglob('*') if p.is_dir()), key=lambda p: len(p.parts), reverse=True):
            sync_directory(path)
        sync_directory(prepared)
        sync_directory(backup)
        refuse_running_backend(app)
        completed = []
        journal = {'status': 'prepared', 'baseFiles': base, 'targetManifestSha256': target['SHA256SUMS'], 'items': ITEMS}
        write(backup / 'transaction.json', journal)
        try:
            for name in ITEMS:
                present = (app / name).exists()
                write(backup / 'transaction.json', dict(journal, status='replacing', completed=completed, activeItem=[name, present]))
                if present:
                    exchange(app / name, prepared / name)
                else:
                    (prepared / name).rename(app / name)
                completed.append((name, present))
                sync_directory((app / name).parent)
                sync_directory((prepared / name).parent)
                write(backup / 'transaction.json', dict(journal, status='replacing', completed=completed))
            require(verify(app) == target, 'Installed payload differs from staged package')
            write(backup / 'transaction.json', dict(journal, status='installed', completed=completed,
                  scope='Only Core, Go notices, bounded Core provenance and release metadata changed; Host/CLI/Desktop/app notices/integration/private data retained'))
        except BaseException:
            for name, present in reversed(completed):
                if present:
                    exchange(app / name, prepared / name)
                else:
                    (app / name).rename(prepared / name)
                sync_directory((app / name).parent)
                sync_directory((prepared / name).parent)
            require(verify(app) == base, 'Rollback inventory differs; retain backup and inspect transaction.json')
            write(backup / 'transaction.json', dict(journal, status='rolled-back-after-error', completed=completed))
            raise
    print('Installed Core only; no service action performed. Backup: ' + str(backup))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    staging = commands.add_parser('stage')
    for name in ('base', 'core', 'core-build-manifest', 'qualification', 'output'):
        staging.add_argument('--' + name, type=Path, required=True)
    staging.add_argument('--source-commit', required=True)
    staging.add_argument('--archive', type=Path, help='optional verified official source zip for notice preparation')
    installing = commands.add_parser('install')
    for name in ('package', 'home', 'backup'):
        installing.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    (stage if args.command == 'stage' else install)(args)


if __name__ == '__main__':
    main()
